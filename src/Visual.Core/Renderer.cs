using Silk.NET.OpenGL;
using Visual.Core;

namespace Visual.Core;

public sealed class ShaderProgram
{
    public IsfShader Isf = new();
    public uint Id;
    public string? Error, Warning;
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
        attribute vec2 pos;
        varying vec2 isf_FragNormCoord;
        void main() { isf_FragNormCoord = pos * 0.5 + 0.5; gl_Position = vec4(pos, 0.0, 1.0); }
        """;

    readonly GL gl;
    readonly uint vao, vbo, waveTex, fftTex, blackTex;
    readonly ShaderProgram post;
    readonly Dictionary<string, ShaderProgram> cache = [];
    Target sceneA, sceneB, ping, pong;
    int w, h, frameIndex;
    double strobePhase;
    readonly AutoStrobeRunner autoStrobe = new();
    public AutoStrobeRunner AutoStrobe => autoStrobe;

    public double Time { get; private set; }
    public string ShaderFolder = "";
    public bool StrobeHeld;
    /// <summary>Internal resolution as a fraction of the output (slow GPUs): draws smaller, stretched on screen.</summary>
    public float Scale = 1;
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
    }

    public ShaderProgram Get(string file)
    {
        if (cache.TryGetValue(file, out var p)) return p;
        p = new ShaderProgram();
        try
        {
            p.Isf = IsfShader.Parse(File.ReadAllText(Path.Combine(ShaderFolder, file)));
            if (p.Isf.PassCount > 1) p.Warning = $"Tiene {p.Isf.PassCount} pases; por ahora solo se dibuja el primero.";
            p.Id = Compile(p.Isf.BuildFragment(Version));
        }
        catch (Exception e) { p.Error = e.Message.Trim(); }
        return cache[file] = p;
    }

    public void Reload()
    {
        foreach (var p in cache.Values) if (p.Id != 0) gl.DeleteProgram(p.Id);
        cache.Clear();
    }

    /// <summary>Draws into the rectangle (x, y from the top-left, width x height) of a screen of screenW x screenH; the rest is black.</summary>
    public void Render(Player player, AudioAnalyzer audio, float dt, int x, int y, int width, int height, int screenW, int screenH, bool allowFastStrobe)
    {
        if (width <= 0 || height <= 0) return; // minimized
        Time += dt;
        float maxHz = allowFastStrobe ? 20f : EffectDefs.SafeStrobeHz;
        player.Project.EnsureEffects();
        bool sceneStrobe = Fx(player, audio, "strobeRate") > 0;
        if (autoStrobe.Update(player.Project.AutoStrobe, audio.Bands, dt, maxHz, blocked: sceneStrobe) && player.Project.AutoStrobe.NextScene) player.Next();
        StrobeActive = sceneStrobe || autoStrobe.Active || StrobeHeld;
        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.ScissorTest);
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.CullFace);
        EnsureSize(Math.Max(1, (int)(width * Scale)), Math.Max(1, (int)(height * Scale)));
        gl.BindVertexArray(vao);
        gl.Viewport(0, 0, (uint)w, (uint)h);
        Upload(waveTex, audio.Wave);
        Upload(fftTex, audio.Spectrum);

        var scenes = player.Project.Scenes;
        DrawScene(player.Current >= 0 ? scenes[player.Current] : null, player.SceneTime, dt, sceneA, audio);
        bool fading = player.Previous >= 0;
        if (fading) DrawScene(scenes[player.Previous], player.PrevTime, dt, sceneB, audio);
        DrawPost(player, audio, dt, fading ? 1 - player.Fade : 0, maxHz);

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, (uint)screenW, (uint)screenH);
        gl.ClearColor(0, 0, 0, 1);
        gl.Clear(ClearBufferMask.ColorBufferBit);
        int y0 = screenH - y - height; // GL counts from the bottom
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, ping.Fbo);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
        gl.BlitFramebuffer(0, 0, w, h, x, y0, x + width, y0 + height, ClearBufferMask.ColorBufferBit,
            w == width && h == height ? BlitFramebufferFilter.Nearest : BlitFramebufferFilter.Linear);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        (ping, pong) = (pong, ping);
        frameIndex++;
    }

    void DrawScene(Scene? scene, double time, float dt, Target target, AudioAnalyzer audio)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, target.Fbo);
        var p = scene == null ? null : Get(scene.Shader);
        if (scene == null || p == null || p.Id == 0)
        {
            gl.ClearColor(0, 0, 0, 1);
            gl.Clear(ClearBufferMask.ColorBufferBit);
            return;
        }
        p.Isf.EnsureParams(scene.Params);
        gl.UseProgram(p.Id);
        var now = DateTime.Now;
        gl.Uniform1(p.Loc(gl, "TIME"), (float)time);
        gl.Uniform1(p.Loc(gl, "TIMEDELTA"), dt);
        gl.Uniform2(p.Loc(gl, "RENDERSIZE"), (float)w, (float)h);
        gl.Uniform1(p.Loc(gl, "FRAMEINDEX"), frameIndex);
        gl.Uniform1(p.Loc(gl, "PASSINDEX"), 0);
        gl.Uniform4(p.Loc(gl, "DATE"), (float)now.Year, (float)now.Month, (float)now.Day, (float)now.TimeOfDay.TotalSeconds);

        int unit = 0;
        foreach (var i in p.Isf.Inputs)
        {
            int loc = p.Loc(gl, i.Name);
            if (i.IsTexture)
            {
                // ponytail: image inputs get a black 1x1 texture until image loading exists
                var (tex, size) = i.Type switch
                {
                    IsfType.Audio => (waveTex, audio.Wave.Length),
                    IsfType.AudioFFT => (fftTex, audio.Spectrum.Length),
                    _ => (blackTex, 1),
                };
                gl.ActiveTexture(TextureUnit.Texture0 + unit);
                gl.BindTexture(TextureTarget.Texture2D, tex);
                gl.Uniform1(loc, unit++);
                gl.Uniform2(p.Loc(gl, $"_{i.Name}_imgSize"), (float)size, 1f);
                gl.Uniform4(p.Loc(gl, $"_{i.Name}_imgRect"), 0f, 0f, (float)size, 1f);
                continue;
            }
            var ps = scene.Params[i.Name];
            var v = ps.Value;
            switch (i.Type)
            {
                case IsfType.Float: gl.Uniform1(loc, ps.Mod.Apply(v[0], i.Min[0], i.Max[0], time, audio.Bands)); break;
                case IsfType.Long: gl.Uniform1(loc, (int)MathF.Round(v[0])); break;
                case IsfType.Bool: gl.Uniform1(loc, v[0] > 0.5f ? 1 : 0); break;
                case IsfType.Event: gl.Uniform1(loc, v[0] > 0.5f ? 1 : 0); v[0] = 0; break; // one-frame pulse
                case IsfType.Point2D: gl.Uniform2(loc, v[0], v[1]); break;
                case IsfType.Color: gl.Uniform4(loc, v[0], v[1], v[2], v[3]); break;
            }
        }
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
    }

    /// <summary>Effect value of the playing scene; during a crossfade it blends from the previous scene's value.</summary>
    float Fx(Player pl, AudioAnalyzer audio, string n)
    {
        var d = EffectDefs.Get(n);
        var sc = pl.Project.Scenes;
        if (pl.Current < 0 || pl.Current >= sc.Count) return d.Default;
        float V(Scene s) { var ps = s.Effects[n]; return ps.Off ? d.Default : ps.Mod.Apply(ps.Value[0], d.Min, d.Max, Time, audio.Bands); }
        float cur = V(sc[pl.Current]);
        if (pl.Previous < 0 || pl.Previous >= sc.Count) return cur;
        float prev = V(sc[pl.Previous]);
        return prev + (cur - prev) * pl.Fade;
    }

    void DrawPost(Player player, AudioAnalyzer audio, float dt, float mixB, float maxHz)
    {
        float E(string n) => Fx(player, audio, n);

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, ping.Fbo);
        gl.UseProgram(post.Id);
        Bind(0, sceneA.Tex, "sceneA");
        Bind(1, mixB > 0 ? sceneB.Tex : sceneA.Tex, "sceneB");
        Bind(2, pong.Tex, "prevOut");
        gl.Uniform1(post.Loc(gl, "mixB"), mixB);
        gl.Uniform2(post.Loc(gl, "RENDERSIZE"), (float)w, (float)h);
        foreach (var d in EffectDefs.All)
            if (!d.Name.StartsWith("strobe")) gl.Uniform1(post.Loc(gl, d.Name), E(d.Name));

        // phase is integrated so an audio-modulated rate doesn't jump around
        // auto strobe bursts and the remote's "hold to strobe" use the auto strobe's own flash settings,
        // otherwise the scene's strobe. Auto bursts never start while the scene strobe is on.
        var a = player.Project.AutoStrobe;
        bool burst = autoStrobe.Active || StrobeHeld;
        float rate = burst ? Math.Clamp(a.Rate, 0.1f, maxHz) : Math.Clamp(E("strobeRate"), 0, maxHz);
        float duty = burst ? Math.Clamp(a.Duty, 0.05f, 0.95f) : E("strobeDuty");
        float intensity = burst ? Math.Clamp(a.Intensity, 0, 1) : E("strobeIntensity");
        float mode = burst ? a.Mode : MathF.Round(E("strobeMode"));
        bool beat = autoStrobe.Active && a.Rhythm == StrobeRhythm.Beat && !StrobeHeld;
        strobePhase = (strobePhase + dt * rate) % 1.0;
        bool on = beat
            ? autoStrobe.SinceHit < duty / Math.Min(maxHz, 10f) // one flash per band hit
            : rate > 0 && strobePhase < duty;
        gl.Uniform1(post.Loc(gl, "strobe"), on ? intensity : 0f);
        gl.Uniform1(post.Loc(gl, "strobeMode"), mode);
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
        foreach (var t in new[] { sceneA, sceneB, ping, pong })
            if (t.Fbo != 0) { gl.DeleteFramebuffer(t.Fbo); gl.DeleteTexture(t.Tex); }
        w = width;
        h = height;
        sceneA = MakeTarget(true);
        sceneB = MakeTarget(true);
        ping = MakeTarget(false);
        pong = MakeTarget(false);
    }

    Target MakeTarget(bool mirror)
    {
        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)w, (uint)h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (void*)null);
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

    uint Compile(string frag)
    {
        uint vs = Shader(ShaderType.VertexShader, VertexSrc), fs = 0;
        try
        {
            fs = Shader(ShaderType.FragmentShader, frag);
            uint p = gl.CreateProgram();
            gl.AttachShader(p, vs);
            gl.AttachShader(p, fs);
            gl.BindAttribLocation(p, 0, "pos");
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
        Reload();
        gl.DeleteProgram(post.Id);
        foreach (var t in new[] { sceneA, sceneB, ping, pong })
            if (t.Fbo != 0) { gl.DeleteFramebuffer(t.Fbo); gl.DeleteTexture(t.Tex); }
        gl.DeleteTexture(waveTex);
        gl.DeleteTexture(fftTex);
        gl.DeleteTexture(blackTex);
        gl.DeleteBuffer(vbo);
        gl.DeleteVertexArray(vao);
    }
}
