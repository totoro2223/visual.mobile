using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using Visual.Core;
using Vortice.MediaFoundation;

namespace Visual.Desktop;

/// <summary>Scene media on the PC: pictures with System.Drawing, videos (files and http URLs) with Media Foundation.</summary>
static class DesktopMedia
{
    public static IMediaSource? Open(GL gl, string folder, string src) => Media.Resolve(folder, src) switch
    {
        null => null,
        var path when Media.IsImage(src) && !Media.IsUrl(src) => new StillImage(gl, path),
        var path => new MfVideo(gl, path),
    };

    internal static unsafe uint Upload(GL gl, uint tex, int w, int h, int rowPixels, byte[] bgra, bool opaque)
    {
        if (tex == 0)
        {
            tex = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, tex);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }
        gl.BindTexture(TextureTarget.Texture2D, tex);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        gl.PixelStore(PixelStoreParameter.UnpackRowLength, rowPixels);
        fixed (byte* p = bgra)
            gl.TexImage2D(TextureTarget.Texture2D, 0, opaque ? InternalFormat.Rgb8 : InternalFormat.Rgba8, (uint)w, (uint)h, 0, PixelFormat.Bgra, PixelType.UnsignedByte, p);
        gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        return tex;
    }
}

sealed class StillImage : IMediaSource
{
    readonly GL gl;
    public uint Texture { get; }
    public int Width { get; }
    public int Height { get; }
    public bool TopDown => true;
    public string? Error { get; }

    public StillImage(GL gl, string path)
    {
        this.gl = gl;
        try
        {
            using var bmp = new System.Drawing.Bitmap(path);
            var r = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var d = bmp.LockBits(r, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var px = new byte[d.Stride * d.Height];
            Marshal.Copy(d.Scan0, px, 0, px.Length);
            bmp.UnlockBits(d);
            (Width, Height) = (bmp.Width, bmp.Height);
            Texture = DesktopMedia.Upload(gl, 0, Width, Height, d.Stride / 4, px, opaque: false);
        }
        catch (Exception e) { Error = $"{Path.GetFileName(path)}: {e.Message}"; }
    }

    public void Update(double dt, float speed) { }
    public void Dispose() { if (Texture != 0) gl.DeleteTexture(Texture); }
}

/// <summary>
/// Decodes on its own thread with a Source Reader (converted to RGB32), paced by the time the GL thread asks for,
/// so the speed is any value. Loops at the end; a broken file / lost stream retries every few seconds.
/// ponytail: frames go through the CPU (fine for 1080p); D3D-GL interop if 4K ever needs it.
/// </summary>
sealed class MfVideo : IMediaSource
{
    static readonly Lazy<bool> Started = new(() => { MediaFactory.MFStartup(); return true; });

    readonly GL gl;
    readonly string path;
    readonly object gate = new();
    volatile bool stop;
    double want;          // playback time the screen is at (s), advanced by Update
    byte[]? ready, spare; // newest decoded frame / buffer to reuse
    int fw, fh, rowPixels;
    volatile bool topDown = true;
    volatile string? error;

    public uint Texture { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool TopDown { get; private set; }
    public string? Error => error;

    public MfVideo(GL gl, string path)
    {
        this.gl = gl;
        this.path = path;
        new Thread(Run) { IsBackground = true, Name = "video" }.Start();
    }

    public void Update(double dt, float speed)
    {
        byte[]? f;
        int w, h, row;
        lock (gate)
        {
            want += dt * speed;
            (f, ready, w, h, row) = (ready, null, fw, fh, rowPixels);
        }
        if (f == null) return;
        Texture = DesktopMedia.Upload(gl, Texture, w, h, row, f, opaque: true); // RGB32 has no real alpha
        (Width, Height, TopDown) = (w, h, topDown);
        lock (gate) spare = f;
    }

    void Run()
    {
        while (!stop)
        {
            try
            {
                _ = Started.Value;
                Play();
                return;
            }
            catch (Exception e)
            {
                error = $"{(Media.IsUrl(path) ? path : Path.GetFileName(path))}: {e.Message}";
                for (int i = 0; i < 50 && !stop; i++) Thread.Sleep(100); // retry in 5 s
            }
        }
    }

    void Play()
    {
        using var attrs = MediaFactory.MFCreateAttributes(1);
        attrs.Set(SourceReaderAttributeKeys.EnableVideoProcessing, 1u); // lets the reader convert YUV to RGB32
        using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, attrs);
        reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);
        using (var mt = MediaFactory.MFCreateMediaType())
        {
            mt.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            mt.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, mt);
        }
        int w, h, stride;
        using (var cur = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream))
        {
            ulong size = cur.GetUInt64(MediaTypeAttributeKeys.FrameSize);
            (w, h) = ((int)(size >> 32), (int)(size & 0xFFFFFFFF));
            try { stride = (int)cur.GetUInt32(MediaTypeAttributeKeys.DefaultStride); }
            catch { stride = w * 4; }
        }
        if (w <= 0 || h <= 0) throw new InvalidDataException("sin imagen de video");
        error = null;
        double loopStart = 0, last = -1; // "want" time where this loop began; last frame's time inside the loop
        double? t0 = null; // first timestamp: some files don't start at 0
        while (!stop)
        {
            double target;
            lock (gate) target = want;
            if (last >= 0 && loopStart + last > target) { Thread.Sleep(3); continue; } // shown frame is still current
            using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, 0, out _, out var flags, out long ts);
            if ((flags & SourceReaderFlag.EndOfStream) != 0)
            {
                if (last < 0) throw new InvalidDataException("el video no tiene cuadros");
                loopStart += last + 1 / 30.0;
                last = -1;
                reader.SetCurrentPosition(0);
                continue;
            }
            if ((flags & SourceReaderFlag.Error) != 0) throw new IOException("error al leer el video");
            if (sample == null) continue;
            t0 ??= ts / 1e7;
            last = ts / 1e7 - t0.Value;
            Copy(sample, w, h, stride);
        }
    }

    unsafe void Copy(IMFSample sample, int w, int h, int stride)
    {
        using var buf = sample.ConvertToContiguousBuffer();
        buf.Lock(out var ptr, out _, out int len);
        try
        {
            int rowBytes = Math.Abs(stride), n = rowBytes * h;
            if (len < n) return;
            byte[] f;
            lock (gate) { f = spare is { } s && s.Length == n ? s : new byte[n]; spare = null; }
            new ReadOnlySpan<byte>((void*)ptr, n).CopyTo(f);
            lock (gate) { ready = f; fw = w; fh = h; rowPixels = rowBytes / 4; }
            topDown = stride > 0; // negative stride = bottom-up rows, already GL's order
        }
        finally { buf.Unlock(); }
    }

    public void Dispose()
    {
        stop = true; // the thread closes the reader itself
        if (Texture != 0) gl.DeleteTexture(Texture);
        Texture = 0;
    }
}
