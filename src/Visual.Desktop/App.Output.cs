using ImGuiNET;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Visual.Desktop;

/// <summary>
/// Separate output window: images only, full screen on the chosen monitor (projector, TV, a cast target),
/// while this window keeps the panel and a preview. Its GL context shares textures with the main one,
/// so each frame is just copied over.
/// </summary>
sealed partial class App
{
    IWindow? outWin;
    GL? outGl;
    uint outFbo;
    string? outputError;

    IMonitor[] Monitors() => Silk.NET.Windowing.Monitor.GetMonitors(window).ToArray();

    static string MonitorName(IMonitor m) => $"{m.Index + 1}: {m.Name} ({m.Bounds.Size.X} x {m.Bounds.Size.Y})";

    IMonitor? OutputMonitor()
    {
        var all = Monitors();
        return all.Length == 0 ? null : all[Math.Clamp(settings.OutputMonitor, 0, all.Length - 1)];
    }

    (int W, int H)? OutputSize()
    {
        if (outWin is not { IsClosing: false }) return null;
        var s = outWin.FramebufferSize;
        return s.X > 0 && s.Y > 0 ? (s.X, s.Y) : null; // minimized
    }

    void OpenOutput()
    {
        CloseOutput();
        outputError = null;
        if (OutputMonitor() is not { } m) { outputError = "No se encontraron monitores"; return; }
        try
        {
            outWin = Window.Create(WindowOptions.Default with
            {
                Title = "Visual - salida",
                Position = m.Bounds.Origin,
                Size = m.Bounds.Size,
                WindowBorder = WindowBorder.Hidden,
                VSync = true, // the output paces the frames; the panel window stops waiting for vsync
                ShouldSwapAutomatically = false,
                SharedContext = window.GLContext,
                API = window.API,
            });
            outWin.Initialize();
            outWin.GLContext!.MakeCurrent();
            outGl = GL.GetApi(outWin.GLContext);
            outFbo = outGl.GenFramebuffer(); // framebuffers aren't shared between contexts; textures are
            window.VSync = false;
        }
        catch (Exception e)
        {
            outputError = "No se pudo abrir la salida: " + e.Message;
            CloseOutput();
        }
        window.GLContext!.MakeCurrent();
    }

    void CloseOutput()
    {
        if (outWin == null) return;
        try
        {
            outWin.GLContext?.MakeCurrent();
            if (outFbo != 0) outGl?.DeleteFramebuffer(outFbo);
            outWin.Dispose();
        }
        catch { } // already gone
        outWin = null;
        outGl = null;
        outFbo = 0;
        window.GLContext!.MakeCurrent();
        window.VSync = true;
    }

    void DrawOutput()
    {
        if (outWin == null || outGl == null) return;
        outWin.DoEvents();
        if (outWin.IsClosing) // closed with Alt+F4
        {
            settings.SeparateOutput = false;
            CloseOutput();
            return;
        }
        gl.Flush(); // the frame has to be finished before the other context reads it
        outWin.GLContext!.MakeCurrent();
        var fb = outWin.FramebufferSize;
        var (w, h) = renderer.OutputSize;
        outGl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, outFbo);
        outGl.FramebufferTexture2D(FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, renderer.OutputTexture, 0);
        outGl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
        outGl.Viewport(0, 0, (uint)fb.X, (uint)fb.Y);
        outGl.ClearColor(0, 0, 0, 1);
        outGl.Clear(ClearBufferMask.ColorBufferBit);
        var p = renderer.Placed; // the scene's own canvas, if any
        int bx = (int)MathF.Round(p.X * fb.X), bw = Math.Max(1, (int)MathF.Round(p.W * fb.X)), bh = Math.Max(1, (int)MathF.Round(p.H * fb.Y));
        int by = fb.Y - (int)MathF.Round(p.Y * fb.Y) - bh; // GL counts from the bottom
        outGl.BlitFramebuffer(0, 0, w, h, bx, by, bx + bw, by + bh, ClearBufferMask.ColorBufferBit,
            w == bw && h == bh ? BlitFramebufferFilter.Nearest : BlitFramebufferFilter.Linear);
        outGl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        outWin.GLContext.SwapBuffers();
        window.GLContext!.MakeCurrent();
    }

    void OutputSettings()
    {
        var mons = Monitors();
        var names = mons.Select(MonitorName).ToArray();
        int cur = Math.Clamp(settings.OutputMonitor, 0, Math.Max(0, mons.Length - 1));
        if (ImGui.Combo("Monitor", ref cur, names, names.Length))
        {
            settings.OutputMonitor = cur;
            if (outWin != null) OpenOutput(); // move it
        }
        bool sep = settings.SeparateOutput;
        if (ImGui.Checkbox("Ventana de salida aparte (el panel queda en esta)", ref sep))
        {
            settings.SeparateOutput = sep;
            if (sep) OpenOutput();
            else CloseOutput();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Abre otra ventana solo con la imagen, a pantalla completa en ese monitor.\nAlt+F4 sobre ella la cierra.");
        if (!sep && mons.Length > 0 && ImGui.Button("Pantalla completa en ese monitor (oculta el panel)"))
        {
            if (fullscreen) ToggleFullscreen();
            ToggleFullscreen(mons[cur]);
            showUi = false;
        }
        if (outputError != null) Colored(Red, outputError);
        ImGui.TextDisabled("F1 muestra el panel · F11 / Esc sale de pantalla completa.");
        ImGui.TextDisabled("Para un TV / Chromecast: Chrome → Transmitir → Transmitir escritorio,");
        ImGui.TextDisabled("o Win+K (Miracast). Después elegí esa pantalla acá.");
    }
}
