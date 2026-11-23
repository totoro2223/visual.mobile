using Silk.NET.OpenGL;
using Visual.Core;

namespace Visual.Core;

public sealed class ShaderProgram
{
    public IsfShader Isf = new();
    public uint Id;
    public string? Error;
    readonly Dictionary<string, int> locs = [];

    public int Loc(GL gl, string name)
    {
        if (!locs.TryGetValue(name, out var l)) locs[name] = l = gl.GetUniformLocation(Id, name);
        return l;
    }
}

/// <summary>Scene shader(s) -> offscreen -> post effects (ping-pong for feedback) -> screen.</summary>
public sealed unsafe class Renderer : IDisposable
{
    record struct Target(uint Fbo, uint Tex);

    /// <summary>GLSL header for desktop: ISF shaders are GLSL 1.x; needs a compatibility-profile context.</summary>
    public const string DesktopHeader = "#version 120\n";
    /// <summary>GLSL ES 1.00 header, for an OpenGL ES 3 context (Android).</summary>
    public const string EsHeader = "#ifdef GL_FRAGMENT_PRECISION_HIGH\nprecision highp float;\n#else\nprecision mediump float;\n#endif\n";

    readonly string Version;
    string VertexSrc => Version + """
        attribute vec2 isf_Position;
        varying vec2 isf_FragNormCoord;
        void main() { isf_FragNormCoord = isf_Position * 0.5 + 0.5; gl_Position = vec4(isf_Position, 0.0, 1.0); }
        """;

    readonly GL gl;
    readonly uint vao, vbo, waveTex, fftTex, blackTex;
    readonly ShaderProgram post, blend;
    readonly Dictionary<string, ShaderProgram> cache = [];
    Target sceneA, sceneB, ping, pong;
    Target layerRaw, layerFx, spare; // layers: its shader, its effects, the mix (swapped with the scene's picture)
    float maxHzNow = EffectDefs.SafeStrobeHz, strobeScale = 1;
    int w, h, frameIndex;
    double strobePhase;
    bool strobeWasOn;
    float randomHue;
    readonly AutoStrobeRunner autoStrobe = new();
    public AutoStrobeRunner AutoStrobe => autoStrobe;
    readonly AutoDjRunner autoDj = new();
    public AutoDjRunner AutoDj => autoDj;

    public double Time { get; private set; }
    public string ShaderFolder = "";
    public bool StrobeHeld;
    /// <summary>Internal resolution as a fraction of the output (slow GPUs): draws smaller, stretched on screen.</summary>
    public float Scale = 1;
    /// <summary>The last finished frame (a texture, shareable with another GL context) and its size.</summary>
    public uint OutputTexture => pong.Tex;
    public (int W, int H) OutputSize => (w, h);
    /// <summary>Some strobe is running (scene, auto burst or remote hold); for the on-screen indicator.</summary>
    public bool StrobeActive { get; private set; }

    public Renderer(GL gl, string header)
    {
        this.gl = gl;
        Version = header;
        vao = gl.GenVertexArray();
        gl.BindVertexArray(vao);
        vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        float[] quad = [-1, -1, 1, -1, -1, 1, 1, 1];
        fixed (float* p = quad)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 0, (void*)0);

        waveTex = DataTexture();
        fftTex = DataTexture();
        blackTex = DataTexture();
        Upload(blackTex, [0]);
        post = new ShaderProgram { Id = Compile(EffectDefs.PostFragment(Version)) };
        blend = new ShaderProgram { Id = Compile(Blends.Fragment(Version)) };
    }

    public ShaderProgram Get(string file)
    {
        if (cache.TryGetValue(file, out var p)) return p;
        p = new ShaderProgram();
        try
        {
            p.Isf = IsfShader.Load(ShaderFolder, file);
            p.Id = Compile(p.Isf.BuildFragment(Version), p.Isf.BuildVertex(Version));
        }
        catch (Exception e) { p.Error = e.Message.Trim(); }
        return cache[file] = p;
    }

    public void Reload()
    {
        foreach (var p in cache.Values) if (p.Id != 0) gl.DeleteProgram(p.Id);
        cache.Clear();
        foreach (var b in buffers.Values) b.Free(gl);
        buffers.Clear();
    }

    /// <summary>A scene's pass buffers: two textures each (read the last frame's, write the other, then swap).</summary>
    sealed class PassBuffers(ShaderProgram owner, int w, int h)
    {
        public readonly ShaderProgram Owner = owner;
        public readonly int W = w, H = h;
        public readonly Dictionary<string, (Target Front, Target Back)> Of = [];
        public int Used;

        public void Free(GL gl)
        {
            foreach (var (a, b) in Of.Values)
                foreach (var t in new[] { a, b }) { gl.DeleteFramebuffer(t.Fbo); gl.DeleteTexture(t.Tex); }
        }
    }
    readonly Dictionary<Picture, PassBuffers> buffers = [];

    PassBuffers BuffersFor(Picture scene, ShaderProgram p)
    {
        if (buffers.TryGetValue(scene, out var b) && b.Owner == p && b.W == w && b.H == h) return b;
        b?.Free(gl); // another shader or size: start over (black)
        b = new PassBuffers(p, w, h);
        foreach (var n in p.Isf.Buffers) b.Of[n] = (MakeTarget(false), MakeTarget(false));
        return buffers[scene] = b;
    }

    /// <summary>
    /// Canvas rectangle on a screen of sw x sh: "full", an aspect ratio such as "16:9" (largest centered fit),
    /// or "px" (x, y, w, h in pixels from the top-left, clipped to the screen).
    /// </summary>
    public static (int X, int Y, int W, int H) Fit(string mode, int x, int y, int w, int h, int sw, int sh)
    {
        if (sw <= 0 || sh <= 0) return (0, 0, sw, sh);
        if (mode == "px")
        {
            x = Math.Clamp(x, 0, sw - 1);
            y = Math.Clamp(y, 0, sh - 1);
            return (x, y, Math.Clamp(w, 1, sw - x), Math.Clamp(h, 1, sh - y));
        }
        var p = mode.Split(':');
        if (p.Length == 2 && float.TryParse(p[0], System.Globalization.CultureInfo.InvariantCulture, out var a)
            && float.TryParse(p[1], System.Globalization.CultureInfo.InvariantCulture, out var b) && a > 0 && b > 0)
        {
            float s = Math.Min(sw / a, sh / b);
            int fw = Math.Clamp((int)Math.Round(a * s), 1, sw), fh = Math.Clamp((int)Math.Round(b * s), 1, sh);
            return ((sw - fw) / 2, (sh - fh) / 2, fw, fh);
        }
        return (0, 0, sw, sh);
    }

    /// <summary>Size drawn at for an output of w x h: Scale of it (0.25..1), at least a pixel.</summary>
    public static (float W, float H) InternalSize(int w, int h, float scale)
    {
        float s = float.IsFinite(scale) ? Math.Clamp(scale, 0.25f, 1f) : 1;
        return (Math.Max(1, w * s), Math.Max(1, h * s));
    }

    /// <summary>Where the last frame went, as fractions of the area given to Render (the output window copies it there too).</summary>
    public (float X, float Y, float W, float H) Placed { get; private set; } = (0, 0, 1, 1);

    /// <summary>
    /// Canvas of the playing scene as fractions of an area refW x refH: its own canvas, or <paramref name="global"/> if it has none.
    /// During a crossfade the shown rectangle slides from the old scene's to the new one's.
    /// </summary>
    public static ((float X, float Y, float W, float H) Cur, (float X, float Y, float W, float H) Shown) Place(
        Player pl, (float X, float Y, float W, float H) global, int refW, int refH)
    {
        var sc = pl.Project.Scenes;
        (float X, float Y, float W, float H) Of(int i)
        {
            if (i < 0 || i >= sc.Count || sc[i].Canvas is not { } c || refW <= 0 || refH <= 0) return global;
            var (x, y, w, h) = Fit(c.Mode, c.X, c.Y, c.W, c.H, refW, refH);
            return (x / (float)refW, y / (float)refH, w / (float)refW, h / (float)refH);
        }
        var cur = Of(pl.Current);
        if (pl.Previous < 0) return (cur, cur);
        var (a, t) = (Of(pl.Previous), pl.Fade);
        return (cur, (a.X + (cur.X - a.X) * t, a.Y + (cur.Y - a.Y) * t, a.W + (cur.W - a.W) * t, a.H + (cur.H - a.H) * t));
    }

    /// <summary>Draws into the area (x, y from the top-left, width x height) of a screen of screenW x screenH; the rest is black.</summary>
    /// <param name="renderW">output size when it isn't the on-screen size (a separate output window), drawn at Scale of it too; 0 = width</param>
    /// <param name="global">canvas in pixels of the area for scenes without their own; null = the whole area</param>
    public void Render(Player player, AudioAnalyzer audio, float dt, int x, int y, int width, int height, int screenW, int screenH, bool allowFastStrobe,
        int renderW = 0, int renderH = 0, (int X, int Y, int W, int H)? global = null)
    {
        if (width <= 0 || height <= 0) return; // minimized
        Time += dt;
        float maxHz = allowFastStrobe ? 20f : EffectDefs.SafeStrobeHz;
        player.Project.EnsureEffects();
        bool sceneStrobe = Fx(player, audio, "strobeRate") > 0;
        if (autoStrobe.Update(player.Project.AutoStrobe, audio.Bands, dt, maxHz, blocked: sceneStrobe)
            && autoStrobe.Ended(player.Project.AutoStrobe)?.NextScene == true) player.Next();
        autoDj.Update(player.Project.AutoDj, player, audio.Music, dt);
        StrobeActive = sceneStrobe || autoStrobe.Active || StrobeHeld;
        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.ScissorTest);
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.CullFace);
        var g = global is { } gr ? (gr.X / (float)width, gr.Y / (float)height, gr.W / (float)width, gr.H / (float)height) : (0f, 0f, 1f, 1f);
        var (cur, f) = Place(player, g, renderW > 0 ? renderW : width, renderH > 0 ? renderH : height);
        // sized for the new scene's canvas; during a fade the picture stretches a little while the rectangle slides
        float q = player.Current >= 0 ? player.Project.Scenes[player.Current].Quality : 0; // the scene's own quality, if any
        var (iw, ih) = InternalSize(renderW > 0 ? renderW : width, renderH > 0 ? renderH : height, q > 0 ? q : Scale);
        EnsureSize(Math.Max(1, (int)(iw * cur.W)), Math.Max(1, (int)(ih * cur.H)));
        gl.BindVertexArray(vao);
        gl.Viewport(0, 0, (uint)w, (uint)h);
        Upload(waveTex, audio.Wave);
        Upload(fftTex, audio.Spectrum);

        var scenes = player.Project.Scenes;
        maxHzNow = maxHz;
        // ponytail: the safety limit counts every strobe's rate added up (an upper bound: flashes that coincide count twice)
        float total = StrobeTotal(player, Time, audio.Bands, maxHz, SceneStrobeRate(player, audio, maxHz));
        strobeScale = total > maxHz ? maxHz / total : 1;
        DrawScene(player.Current >= 0 ? scenes[player.Current] : null, player.SceneTime, dt, ref sceneA, audio);
        bool fading = player.Previous >= 0;
        if (fading) DrawScene(scenes[player.Previous], player.PrevTime, dt, ref sceneB, audio);
        DrawPost(player, audio, dt, fading ? 1 - player.Fade : 0, maxHz);
        MeasureMotion(player, dt);
        TakeThumb(player);

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, (uint)screenW, (uint)screenH);
        gl.ClearColor(0, 0, 0, 1);
        gl.Clear(ClearBufferMask.ColorBufferBit);
        int bx = x + (int)MathF.Round(f.X * width), by = y + (int)MathF.Round(f.Y * height);
        int bw = Math.Max(1, (int)MathF.Round(f.W * width)), bh = Math.Max(1, (int)MathF.Round(f.H * height));
        int y0 = screenH - by - bh; // GL counts from the bottom
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, ping.Fbo);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
        gl.BlitFramebuffer(0, 0, w, h, bx, y0, bx + bw, y0 + bh, ClearBufferMask.ColorBufferBit,
            w == bw && h == bh ? BlitFramebufferFilter.Nearest : BlitFramebufferFilter.Linear);
        Placed = f;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        (ping, pong) = (pong, ping);
        // a video not drawn for half a second (scene ended / removed) is closed; it starts over next time
        foreach (var (k, s) in media.Where(kv => kv.Value.Used < frameIndex - 30).ToList())
        {
            s.Source?.Dispose();
            media.Remove(k);
        }
        foreach (var (k, b) in buffers.Where(kv => kv.Value.Used < frameIndex - 30).ToList())
        {
            b.Free(gl);
            buffers.Remove(k);
        }
        foreach (var (k, s) in layerStates.Where(kv => kv.Value.Used < frameIndex - 30).ToList())
        {
            s.Free(gl);
            layerStates.Remove(k);
        }
        frameIndex++;
    }

    /// <summary>Opens a scene's media Src (a file name of the host's media folder or a URL). Unset = image inputs stay black.</summary>
    public Func<string, IMediaSource?>? OpenMedia;

    sealed class MediaSlot(string src)
    {
        public readonly string Src = src;
        public IMediaSource? Source;
        public string? Error;
        public int Used = -1;
    }
    readonly Dictionary<(Picture, string), MediaSlot> media = [];

    /// <summary>The scene's video for an input, opened on first use and advanced once per frame.</summary>
    IMediaSource? MediaFor(Picture scene, string input, float dt)
    {
        if (OpenMedia == null || !scene.Media.TryGetValue(input, out var m) || m.Src == "") return null;
        if (!media.TryGetValue((scene, input), out var slot) || slot.Src != m.Src)
        {
            slot?.Source?.Dispose();
            media[(scene, input)] = slot = new MediaSlot(m.Src);
            try { slot.Source = OpenMedia(m.Src); }
            catch (Exception e) { slot.Error = e.Message; }
        }
        if (slot.Used != frameIndex)
        {
            slot.Used = frameIndex;
            try { slot.Source?.Update(dt, Math.Clamp(m.Speed, SceneMedia.MinSpeed, SceneMedia.MaxSpeed)); }
            catch (Exception e) { slot.Error = e.Message; }
        }
        return slot.Source;
    }

    /// <summary>Problem with a scene's video, for the panels; null if fine or not playing.</summary>
    public string? MediaError(Picture scene, string input) =>
        media.TryGetValue((scene, input), out var s) ? s.Error ?? s.Source?.Error : null;

    /// <summary>Closes every video (they reopen when drawn): before deleting or replacing a file.</summary>
    public void ReleaseMedia()
    {
        foreach (var s in media.Values) s.Source?.Dispose();
        media.Clear();
    }

    /// <summary>The scene's base shader, then each layer: its shader, its effects, mixed over what is below with its blend.</summary>
    void DrawScene(Scene? scene, double time, float dt, ref Target target, AudioAnalyzer audio)
    {
        DrawPicture(scene, time, dt, target, audio);
        if (scene == null) return;
        foreach (var l in scene.Layers)
        {
            if (l.Off || Get(l.Shader).Id == 0) continue;
            DrawPicture(l, time, dt, layerRaw, audio, below: target.Tex);
            var st = LayerStateFor(l);
            float E(string n) => Fx(l, n, audio);
            Target into;
            uint prev = blackTex;
            if (E("feedback") > 0) // its own last frame, kept only while it uses feedback
            {
                if (st.Front.Fbo == 0) (st.Front, st.Back) = (MakeTarget(false), MakeTarget(false));
                (into, prev) = (st.Back, st.Front.Tex);
                (st.Front, st.Back) = (st.Back, st.Front);
            }
            else
            {
                st.Free(gl);
                into = layerFx;
            }
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, into.Fbo);
            gl.UseProgram(post.Id);
            Bind(0, layerRaw.Tex, "sceneA");
            Bind(1, layerRaw.Tex, "sceneB");
            Bind(2, prev, "prevOut");
            gl.Uniform1(post.Loc(gl, "mixB"), 0f);
            gl.Uniform1(post.Loc(gl, "layer"), 1f);
            gl.Uniform2(post.Loc(gl, "RENDERSIZE"), (float)w, (float)h);
            foreach (var d in EffectDefs.All)
                if (!d.Name.StartsWith("strobe")) gl.Uniform1(post.Loc(gl, d.Name), E(d.Name));
            float rate = Math.Clamp(E("strobeRate"), 0, maxHzNow) * strobeScale;
            st.StrobePhase = (st.StrobePhase + dt * rate) % 1.0;
            bool on = rate > 0 && st.StrobePhase < E("strobeDuty");
            if (on && !st.StrobeWasOn) st.RandomHue = Random.Shared.NextSingle();
            st.StrobeWasOn = on;
            float mode = MathF.Round(E("strobeMode"));
            gl.Uniform1(post.Loc(gl, "strobe"), on ? E("strobeIntensity") : 0f);
            gl.Uniform1(post.Loc(gl, "strobeMode"), mode);
            gl.Uniform1(post.Loc(gl, "strobeHue"), mode > 3.5f ? st.RandomHue : E("strobeHue"));
            gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

            gl.BindFramebuffer(FramebufferTarget.Framebuffer, spare.Fbo);
            gl.UseProgram(blend.Id);
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.BindTexture(TextureTarget.Texture2D, target.Tex);
            gl.Uniform1(blend.Loc(gl, "below"), 0);
            gl.ActiveTexture(TextureUnit.Texture1);
            gl.BindTexture(TextureTarget.Texture2D, into.Tex);
            gl.Uniform1(blend.Loc(gl, "over"), 1);
            gl.Uniform1(blend.Loc(gl, "mode"), (int)l.Blend);
            gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            gl.ActiveTexture(TextureUnit.Texture0);
            (target, spare) = (spare, target);
        }
    }

    /// <summary>A layer's own last frame (feedback) and strobe.</summary>
    sealed class LayerState
    {
        public Target Front, Back;
        public int W, H, Used;
        public double StrobePhase;
        public bool StrobeWasOn;
        public float RandomHue;

        public void Free(GL gl)
        {
            foreach (var t in new[] { Front, Back }) if (t.Fbo != 0) { gl.DeleteFramebuffer(t.Fbo); gl.DeleteTexture(t.Tex); }
            Front = Back = default;
        }
    }
    readonly Dictionary<Layer, LayerState> layerStates = [];

    LayerState LayerStateFor(Layer l)
    {
        if (!layerStates.TryGetValue(l, out var s)) layerStates[l] = s = new LayerState();
        if (s.W != w || s.H != h) { s.Free(gl); (s.W, s.H) = (w, h); }
        s.Used = frameIndex;
        return s;
    }

    /// <summary>Strobe flashes of the scenes on screen and their layers, per second, before the safety limit.</summary>
    public static float StrobeTotal(Player pl, double time, AudioBands bands, float maxHz, float sceneRate)
    {
        float total = sceneRate;
        foreach (int i in new[] { pl.Current, pl.Previous })
            if (i >= 0 && i < pl.Project.Scenes.Count)
                foreach (var l in pl.Project.Scenes[i].Layers)
                    if (!l.Off && FxAt(l, "strobeIntensity", time, bands) > 0) total += Math.Clamp(FxAt(l, "strobeRate", time, bands), 0, maxHz);
        return total;
    }

    /// <summary>A layer's effect value now (off = its default).</summary>
    float Fx(Picture pic, string n, AudioAnalyzer audio) => FxAt(pic, n, Time, audio.Bands);

    public static float FxAt(Picture pic, string n, double time, AudioBands bands)
    {
        var d = EffectDefs.Get(n);
        var ps = pic.Effects[n];
        return ps.Off ? d.Default : ps.Eval(d.Min, d.Max, time, bands);
    }

    void DrawPicture(Picture? scene, double time, float dt, Target target, AudioAnalyzer audio, uint below = 0)
    {
        var p = scene == null ? null : Get(scene.Shader);
        // videos first: Android draws its frame into a texture with its own framebuffer
        var srcs = p == null || scene == null ? Array.Empty<IMediaSource?>() : p.Isf.Inputs.Select(i => i.Type == IsfType.Image ? MediaFor(scene, i.Name, dt) : null).ToArray();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, target.Fbo);
        gl.Viewport(0, 0, (uint)w, (uint)h);
        if (scene == null || p == null || p.Id == 0)
        {
            gl.ClearColor(0, 0, 0, 1);
            gl.Clear(ClearBufferMask.ColorBufferBit);
            return;
        }
        p.Isf.EnsureParams(scene.Params);
        gl.ActiveTexture(TextureUnit.Texture0);
        var bufs = p.Isf.Buffers.Count > 0 ? BuffersFor(scene, p) : null; // before binding inputs: creating textures rebinds
        if (bufs != null) bufs.Used = frameIndex;
        gl.UseProgram(p.Id);
        var now = DateTime.Now;
        gl.Uniform1(p.Loc(gl, "TIME"), (float)time);
        gl.Uniform1(p.Loc(gl, "TIMEDELTA"), dt);
        gl.Uniform2(p.Loc(gl, "RENDERSIZE"), (float)w, (float)h);
        gl.Uniform1(p.Loc(gl, "FRAMEINDEX"), frameIndex);
        gl.Uniform4(p.Loc(gl, "DATE"), (float)now.Year, (float)now.Month, (float)now.Day, (float)now.TimeOfDay.TotalSeconds);

        int unit = 0;
        for (int k = 0; k < p.Isf.Inputs.Count; k++)
        {
            var i = p.Isf.Inputs[k];
            int loc = p.Loc(gl, i.Name);
            if (i.IsTexture)
            {
                var vid = srcs[k] is { Texture: not 0 } s ? s : null; // no video (or not loaded yet) = black
                var (tex, sw, sh) = i.Type switch
                {
                    IsfType.Audio => (waveTex, audio.Wave.Length, 1),
                    IsfType.AudioFFT => (fftTex, audio.Spectrum.Length, 1),
                    _ when vid != null => (vid.Texture, vid.Width, vid.Height),
                    _ when below != 0 && i.Name == Layer.Below => (below, w, h), // filter layer: what is below it
                    _ => (blackTex, 1, 1),
                };
                gl.ActiveTexture(TextureUnit.Texture0 + unit);
                gl.BindTexture(TextureTarget.Texture2D, tex);
                gl.Uniform1(loc, unit++);
                gl.Uniform2(p.Loc(gl, $"_{i.Name}_imgSize"), (float)sw, (float)sh);
                gl.Uniform4(p.Loc(gl, $"_{i.Name}_imgRect"), 0f, 0f, (float)sw, (float)sh);
                gl.Uniform1(p.Loc(gl, $"_{i.Name}_flip"), vid?.TopDown == true ? 1 : 0);
                continue;
            }
            var ps = scene.Params[i.Name];
            var v = ps.Value;
            switch (i.Type)
            {
                case IsfType.Float: gl.Uniform1(loc, ps.Eval(i.Min[0], i.Max[0], time, audio.Bands)); break;
                case IsfType.Long: gl.Uniform1(loc, (int)MathF.Round(v[0])); break;
                case IsfType.Bool: gl.Uniform1(loc, v[0] > 0.5f ? 1 : 0); break;
                case IsfType.Event: gl.Uniform1(loc, v[0] > 0.5f ? 1 : 0); v[0] = 0; break; // one-frame pulse
                case IsfType.Point2D: gl.Uniform2(loc, v[0], v[1]); break;
                case IsfType.Color: gl.Uniform4(loc, v[0], v[1], v[2], v[3]); break;
            }
        }

        // passes: each draws into its TARGET buffer (or the scene's output), reading every buffer as last left;
        // a final pass with a TARGET is also what the scene shows
        for (int pass = 0; pass < p.Isf.PassCount; pass++)
        {
            var tg = p.Isf.Passes[pass];
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, tg != "" ? bufs!.Of[tg].Back.Fbo : target.Fbo);
            gl.Uniform1(p.Loc(gl, "PASSINDEX"), pass);
            int u = unit;
            if (bufs != null)
                foreach (var (n, (front, _)) in bufs.Of)
                {
                    gl.ActiveTexture(TextureUnit.Texture0 + u);
                    gl.BindTexture(TextureTarget.Texture2D, front.Tex);
                    gl.Uniform1(p.Loc(gl, n), u++);
                    gl.Uniform2(p.Loc(gl, $"_{n}_imgSize"), (float)w, (float)h);
                    gl.Uniform4(p.Loc(gl, $"_{n}_imgRect"), 0f, 0f, (float)w, (float)h);
                    gl.Uniform1(p.Loc(gl, $"_{n}_flip"), 0);
                }
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            if (tg != "") bufs!.Of[tg] = (bufs.Of[tg].Back, bufs.Of[tg].Front);
        }
        if (p.Isf.Passes[^1] is { Length: > 0 } last)
        {
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, bufs!.Of[last].Front.Fbo);
            gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, target.Fbo);
            gl.BlitFramebuffer(0, 0, w, h, 0, 0, w, h, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        }
    }

    /// <summary>Effect value of the playing scene; during a crossfade it blends from the previous scene's value.</summary>
    float Fx(Player pl, AudioAnalyzer audio, string n)
    {
        var d = EffectDefs.Get(n);
        var sc = pl.Project.Scenes;
        if (pl.Current < 0 || pl.Current >= sc.Count) return d.Default;
        float V(Scene s) => Fx(s, n, audio);
        float cur = V(sc[pl.Current]);
        if (pl.Previous < 0 || pl.Previous >= sc.Count) return cur;
        float prev = V(sc[pl.Previous]);
        return prev + (cur - prev) * pl.Fade;
    }

    /// <summary>The scene strobe's (or the auto / held burst's) flashes per second, before the safety scaling.</summary>
    float SceneStrobeRate(Player player, AudioAnalyzer audio, float maxHz) =>
        autoStrobe.Active || StrobeHeld ? Math.Clamp(autoStrobe.Flash(player.Project.AutoStrobe).Rate, 0.1f, maxHz)
        : Fx(player, audio, "strobeIntensity") > 0 ? Math.Clamp(Fx(player, audio, "strobeRate"), 0, maxHz) : 0;

    void DrawPost(Player player, AudioAnalyzer audio, float dt, float mixB, float maxHz)
    {
        float E(string n) => Fx(player, audio, n);

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, ping.Fbo);
        gl.UseProgram(post.Id);
        Bind(0, sceneA.Tex, "sceneA");
        Bind(1, mixB > 0 ? sceneB.Tex : sceneA.Tex, "sceneB");
        Bind(2, pong.Tex, "prevOut");
        gl.Uniform1(post.Loc(gl, "mixB"), mixB);
        gl.Uniform1(post.Loc(gl, "trans"), (float)player.Shape);
        gl.Uniform1(post.Loc(gl, "layer"), 0f);
        gl.Uniform2(post.Loc(gl, "RENDERSIZE"), (float)w, (float)h);
        foreach (var d in EffectDefs.All)
            if (!d.Name.StartsWith("strobe")) gl.Uniform1(post.Loc(gl, d.Name), E(d.Name));

        // phase is integrated so an audio-modulated rate doesn't jump around
        // auto strobe bursts and the remote's "hold to strobe" use the auto strobe's own flash settings,
        // otherwise the scene's strobe. Auto bursts never start while the scene strobe is on.
        var a = autoStrobe.Flash(player.Project.AutoStrobe);
        bool burst = autoStrobe.Active || StrobeHeld;
        float rate = SceneStrobeRate(player, audio, maxHz) * strobeScale;
        float duty = burst ? Math.Clamp(a.Duty, 0.05f, 0.95f) : E("strobeDuty");
        float intensity = burst ? Math.Clamp(a.Intensity, 0, 1) : E("strobeIntensity");
        float mode = burst ? a.Mode : MathF.Round(E("strobeMode"));
        bool beat = autoStrobe.Active && a.Rhythm == StrobeRhythm.Beat && !StrobeHeld;
        strobePhase = (strobePhase + dt * rate) % 1.0;
        bool on = beat
            ? autoStrobe.SinceHit < duty / Math.Min(maxHz, 10f) // one flash per band hit
            : rate > 0 && strobePhase < duty;
        if (on && !strobeWasOn) randomHue = Random.Shared.NextSingle(); // "Color al azar": new color each flash
        strobeWasOn = on;
        gl.Uniform1(post.Loc(gl, "strobe"), on ? intensity : 0f);
        gl.Uniform1(post.Loc(gl, "strobeMode"), mode);
        gl.Uniform1(post.Loc(gl, "strobeHue"), mode > 3.5f ? randomHue : burst ? a.Hue : E("strobeHue"));
        gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        gl.ActiveTexture(TextureUnit.Texture0);
    }

    void Bind(int unit, uint tex, string name)
    {
        gl.ActiveTexture(TextureUnit.Texture0 + unit);
        gl.BindTexture(TextureTarget.Texture2D, tex);
        gl.Uniform1(post.Loc(gl, name), unit);
    }

    void EnsureSize(int width, int height)
    {
        if (width == w && height == h) return;
        foreach (var t in new[] { sceneA, sceneB, ping, pong, layerRaw, layerFx, spare })
            if (t.Fbo != 0) { gl.DeleteFramebuffer(t.Fbo); gl.DeleteTexture(t.Tex); }
        w = width;
        h = height;
        sceneA = MakeTarget(true);
        sceneB = MakeTarget(true);
        ping = MakeTarget(false);
        pong = MakeTarget(false);
        layerRaw = MakeTarget(true);
        layerFx = MakeTarget(false);
        spare = MakeTarget(true);
    }

    // ---- how much the playing scene moves (Scene.Motion, the Auto DJ's guess of its intensity) ----
    const int ProbeW = 32, ProbeH = 18;
    Target probe;
    readonly byte[] probeNow = new byte[ProbeW * ProbeH * 4];
    readonly byte[] probePrev = new byte[ProbeW * ProbeH * 4];
    double probeDue;
    Scene? probeScene;

    /// <summary>Every 1/4 s, the scene picture (before effects) on a thumbnail vs the previous one.</summary>
    // ponytail: ReadPixels waits for the GPU 4 times a second; a pixel buffer object if it shows on the TV
    void MeasureMotion(Player pl, float dt)
    {
        if ((probeDue -= dt) > 0) return;
        probeDue = 0.25;
        var sc = pl.Project.Scenes;
        var s = pl.Previous < 0 && pl.Current >= 0 && pl.Current < sc.Count && pl.SceneTime > 1 ? sc[pl.Current] : null;
        if (s == null || Get(s.Shader).Id == 0) { probeScene = null; return; }
        if (probe.Fbo == 0) probe = MakeTarget(false, ProbeW, ProbeH);
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, sceneA.Fbo);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, probe.Fbo);
        gl.BlitFramebuffer(0, 0, w, h, 0, 0, ProbeW, ProbeH, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear);
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, probe.Fbo);
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        fixed (byte* p = probeNow) gl.ReadPixels(0, 0, ProbeW, ProbeH, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        if (probeScene == s)
        {
            float d = Difference(probeNow, probePrev);
            s.Motion = s.Motion < 0 ? d : s.Motion + (d - s.Motion) * 0.05f; // settles over ~5 s of playing
        }
        probeScene = s;
        probeNow.CopyTo(probePrev, 0);
    }

    Target thumbTarget;
    readonly byte[] thumbPx = new byte[Thumbs.W * Thumbs.H * 4];
    Scene? thumbOf;

    /// <summary>Once each time a scene plays, 2 s in and between strobe flashes: the finished frame as its thumbnail.</summary>
    void TakeThumb(Player pl)
    {
        var sc = pl.Project.Scenes;
        var s = pl.Current >= 0 && pl.Current < sc.Count ? sc[pl.Current] : null;
        if (s == null || pl.SceneTime < 2) { thumbOf = null; return; }
        if (thumbOf == s || pl.Previous >= 0 || strobeWasOn || Get(s.Shader).Id == 0) return;
        thumbOf = s;
        if (thumbTarget.Fbo == 0) thumbTarget = MakeTarget(false, Thumbs.W, Thumbs.H);
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, ping.Fbo);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, thumbTarget.Fbo);
        gl.BlitFramebuffer(0, 0, w, h, 0, 0, Thumbs.W, Thumbs.H, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear);
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, thumbTarget.Fbo);
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        fixed (byte* p = thumbPx) gl.ReadPixels(0, 0, Thumbs.W, Thumbs.H, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        s.Thumb = Thumbs.Pack(thumbPx);
    }

    /// <summary>Mean change of the color channels of two RGBA pictures, 0..1.</summary>
    public static float Difference(byte[] a, byte[] b)
    {
        long sum = 0;
        for (int i = 0; i < a.Length; i++)
            if (i % 4 != 3) sum += Math.Abs(a[i] - b[i]);
        return sum / (255f * a.Length * 3 / 4);
    }

    Target MakeTarget(bool mirror, int tw = 0, int th = 0)
    {
        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)(tw > 0 ? tw : w), (uint)(th > 0 ? th : h), 0, PixelFormat.Rgba, PixelType.UnsignedByte, (void*)null);
        int wrap = (int)(mirror ? TextureWrapMode.MirroredRepeat : TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrap);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrap);
        uint fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, tex, 0);
        gl.ClearColor(0, 0, 0, 1);
        gl.Clear(ClearBufferMask.ColorBufferBit);
        return new(fbo, tex);
    }

    uint DataTexture()
    {
        uint t = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, t);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return t;
    }

    void Upload(uint tex, byte[] data)
    {
        gl.BindTexture(TextureTarget.Texture2D, tex);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (byte* p = data)
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8, (uint)data.Length, 1, 0, PixelFormat.Red, PixelType.UnsignedByte, p);
    }

    uint Compile(string frag, string? vert = null)
    {
        uint vs = Shader(ShaderType.VertexShader, vert ?? VertexSrc), fs = 0;
        try
        {
            fs = Shader(ShaderType.FragmentShader, frag);
            uint p = gl.CreateProgram();
            gl.AttachShader(p, vs);
            gl.AttachShader(p, fs);
            gl.BindAttribLocation(p, 0, "isf_Position"); // not "pos": shaders have inputs called that
            gl.LinkProgram(p);
            gl.GetProgram(p, ProgramPropertyARB.LinkStatus, out int ok);
            if (ok == 0)
            {
                var log = gl.GetProgramInfoLog(p);
                gl.DeleteProgram(p);
                throw new Exception(log);
            }
            return p;
        }
        finally
        {
            gl.DeleteShader(vs);
            if (fs != 0) gl.DeleteShader(fs);
        }
    }

    uint Shader(ShaderType type, string src)
    {
        uint s = gl.CreateShader(type);
        gl.ShaderSource(s, src);
        gl.CompileShader(s);
        gl.GetShader(s, ShaderParameterName.CompileStatus, out int ok);
        if (ok == 0)
        {
            var log = gl.GetShaderInfoLog(s);
            gl.DeleteShader(s);
            throw new Exception(log);
        }
        return s;
    }

    public void Dispose()
    {
        ReleaseMedia();
        Reload(); // also the pass buffers
        gl.DeleteProgram(post.Id);
        gl.DeleteProgram(blend.Id);
        foreach (var s in layerStates.Values) s.Free(gl);
        foreach (var t in new[] { sceneA, sceneB, ping, pong, probe, thumbTarget, layerRaw, layerFx, spare })
            if (t.Fbo != 0) { gl.DeleteFramebuffer(t.Fbo); gl.DeleteTexture(t.Tex); }
        gl.DeleteTexture(waveTex);
        gl.DeleteTexture(fftTex);
        gl.DeleteTexture(blackTex);
        gl.DeleteBuffer(vbo);
        gl.DeleteVertexArray(vao);
    }
}
