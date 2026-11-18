using Android.Graphics;
using PixelFormat = Silk.NET.OpenGL.PixelFormat;
using Android.Media;
using Android.Opengl;
using Android.Views;
using Silk.NET.OpenGL;
using Visual.Core;

namespace VisualApp;

/// <summary>Scene media on Android: pictures with BitmapFactory, videos (files and http / HLS URLs) with MediaPlayer.</summary>
static class AndroidMedia
{
    public static IMediaSource? Open(GL gl, string folder, string src) => Media.Resolve(folder, src) switch
    {
        null => null,
        var path when Media.IsImage(src) && !Media.IsUrl(src) => new AndroidImage(gl, path),
        var path => new AndroidVideo(gl, path),
    };

    internal static uint NewTexture(GL gl, TextureTarget target)
    {
        uint t = gl.GenTexture();
        gl.BindTexture(target, t);
        gl.TexParameter(target, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(target, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(target, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(target, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return t;
    }
}

sealed class AndroidImage : IMediaSource
{
    readonly GL gl;
    public uint Texture { get; }
    public int Width { get; }
    public int Height { get; }
    public bool TopDown => true;
    public string? Error { get; }

    public AndroidImage(GL gl, string path)
    {
        this.gl = gl;
        using var bmp = BitmapFactory.DecodeFile(path);
        if (bmp == null) { Error = $"{System.IO.Path.GetFileName(path)}: no se pudo leer la imagen"; return; }
        (Width, Height) = (bmp.Width, bmp.Height);
        Texture = AndroidMedia.NewTexture(gl, TextureTarget.Texture2D);
        GLUtils.TexImage2D(GLES20.GlTexture2d, 0, bmp, 0);
    }

    public void Update(double dt, float speed) { }
    public void Dispose() { if (Texture != 0) gl.DeleteTexture(Texture); }
}

/// <summary>
/// MediaPlayer decodes (in hardware) into a SurfaceTexture; each new frame is copied from that external
/// texture into a normal one, because ISF shaders only know sampler2D. Looped, muted; retries after an error.
/// </summary>
sealed class AndroidVideo : Java.Lang.Object, IMediaSource, SurfaceTexture.IOnFrameAvailableListener
{
    const TextureTarget External = (TextureTarget)0x8D65; // GL_TEXTURE_EXTERNAL_OES

    readonly GL gl;
    readonly string src;
    readonly uint oes, fbo, prog;
    readonly SurfaceTexture st;
    readonly Surface surface;
    readonly float[] matrix = new float[16];
    MediaPlayer? mp;
    volatile bool newFrame, prepared, failed;
    volatile string? error;
    float speedSet = -1;
    double clock, retryAt;

    public uint Texture { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool TopDown => false; // the copy below already writes GL's row order
    public string? Error => error;

    public AndroidVideo(GL gl, string src)
    {
        this.gl = gl;
        this.src = src;
        oes = AndroidMedia.NewTexture(gl, External);
        st = new SurfaceTexture((int)oes);
        st.SetOnFrameAvailableListener(this);
        surface = new Surface(st);
        fbo = gl.GenFramebuffer();
        prog = CopyProgram(gl);
        Start();
    }

    string Shown => Media.IsUrl(src) ? src : System.IO.Path.GetFileName(src);

    void Start()
    {
        prepared = failed = false;
        speedSet = -1;
        var p = new MediaPlayer();
        mp = p;
        try
        {
            p.SetSurface(surface);
            p.Looping = true;
            p.SetVolume(0, 0); // the sound comes from what the visualizer listens to
            p.Prepared += (_, _) =>
            {
                if (mp != p) return;
                prepared = true;
                error = null;
                p.Start();
            };
            p.Error += (_, e) =>
            {
                error = $"{Shown}: no se pudo reproducir (código {e.What}, {e.Extra})";
                failed = true;
                e.Handled = true;
            };
            p.SetDataSource(src);
            p.PrepareAsync(); // callbacks arrive on the main thread
        }
        catch (Exception e)
        {
            error = $"{Shown}: {e.Message}";
            failed = true;
        }
    }

    public void OnFrameAvailable(SurfaceTexture? surfaceTexture) => newFrame = true;

    public void Update(double dt, float speed)
    {
        clock += dt;
        if (failed)
        {
            if (retryAt == 0) retryAt = clock + 5;
            else if (clock >= retryAt) { retryAt = 0; Stop(); Start(); }
            return;
        }
        if (prepared && mp is { } p && speed != speedSet)
        {
            speedSet = speed;
            try { p.PlaybackParams = new PlaybackParams().SetSpeed(speed)!; }
            catch (Exception e) { error = $"{Shown}: este equipo no admite la velocidad x{speed:0.##} ({e.Message})"; }
        }
        if (!newFrame) return;
        newFrame = false;
        st.UpdateTexImage();
        st.GetTransformMatrix(matrix);
        int w = mp?.VideoWidth ?? 0, h = mp?.VideoHeight ?? 0;
        if (w <= 0 || h <= 0) return;
        Copy(w, h);
    }

    unsafe void Copy(int w, int h)
    {
        if (Texture == 0 || w != Width || h != Height)
        {
            if (Texture != 0) gl.DeleteTexture(Texture);
            Texture = AndroidMedia.NewTexture(gl, TextureTarget.Texture2D);
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)w, (uint)h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (void*)null);
            (Width, Height) = (w, h);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, Texture, 0);
        }
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        gl.Viewport(0, 0, (uint)w, (uint)h);
        gl.UseProgram(prog);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(External, oes);
        gl.Uniform1(gl.GetUniformLocation(prog, "tex"), 0);
        fixed (float* m = matrix) gl.UniformMatrix4(gl.GetUniformLocation(prog, "st"), 1, false, m);
        gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4); // the renderer's quad is bound at attribute 0
    }

    static uint CopyProgram(GL gl)
    {
        uint Sh(ShaderType t, string s)
        {
            uint id = gl.CreateShader(t);
            gl.ShaderSource(id, s);
            gl.CompileShader(id);
            return id;
        }
        uint vs = Sh(ShaderType.VertexShader, """
            attribute vec2 pos;
            uniform mat4 st;
            varying vec2 uv;
            void main() { uv = (st * vec4(pos * 0.5 + 0.5, 0.0, 1.0)).xy; gl_Position = vec4(pos, 0.0, 1.0); }
            """);
        uint fs = Sh(ShaderType.FragmentShader, """
            #extension GL_OES_EGL_image_external : require
            precision mediump float;
            uniform samplerExternalOES tex;
            varying vec2 uv;
            void main() { gl_FragColor = texture2D(tex, uv); }
            """);
        uint p = gl.CreateProgram();
        gl.AttachShader(p, vs);
        gl.AttachShader(p, fs);
        gl.BindAttribLocation(p, 0, "pos");
        gl.LinkProgram(p);
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        gl.GetProgram(p, ProgramPropertyARB.LinkStatus, out int ok);
        if (ok == 0) throw new Exception("copia de video: " + gl.GetProgramInfoLog(p));
        return p;
    }

    void Stop()
    {
        var p = mp;
        mp = null;
        try { p?.Release(); } catch { }
        p?.Dispose();
    }

    void IDisposable.Dispose() // the renderer calls this on the GL thread; Java.Lang.Object's own Dispose is left alone
    {
        Stop();
        surface.Release();
        st.Release();
        gl.DeleteProgram(prog);
        gl.DeleteFramebuffer(fbo);
        gl.DeleteTexture(oes);
        if (Texture != 0) gl.DeleteTexture(Texture);
        Texture = 0;
        base.Dispose();
    }
}
