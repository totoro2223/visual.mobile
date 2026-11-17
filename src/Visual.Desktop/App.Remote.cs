using System.Collections;
using System.Numerics;
using System.Security.Cryptography;
using ImGuiNET;
using QRCoder;
using Visual.Core;

namespace Visual.Desktop;

/// <summary>Wi-Fi remote: the phone opens a page served by the app (see Visual.Core/Remote.cs).</summary>
sealed partial class App
{
    readonly RemoteServer remote = new();
    double remoteClock, strobeUntil, stateDue;
    string remoteUrl = "", qrFor = "";
    List<BitArray> qr = [];

    void StartRemote(bool newToken = false)
    {
        if (newToken || settings.RemoteToken == null) settings.RemoteToken = RandomNumberGenerator.GetHexString(10, true);
        remote.Start(settings.RemotePort, settings.RemoteToken);
        remoteUrl = $"http://{RemoteServer.LocalAddress()}:{remote.Port}/?k={remote.Token}";
    }

    void RemoteFrame(float dt)
    {
        remoteClock += dt;
        renderer.StrobeHeld = remoteClock < strobeUntil;
        if (!remote.Running) return;
        while (remote.Commands.TryDequeue(out var c))
        {
            try
            {
                switch (Remote.Apply(c, player, shaderFiles))
                {
                    case "strobe":
                        // the phone re-sends every 0.4 s while held; if it goes quiet (lost Wi-Fi) the strobe stops by itself
                        strobeUntil = c["on"]?.GetValue<bool>() == true ? remoteClock + 1 : 0;
                        break;
                    case "save": SaveProject(); break;
                    case "open":
                        if (c["name"]?.GetValue<string>() is { } n && projectNames.Contains(n) && n != loadedName)
                        {
                            SaveProject();
                            OpenProject(n);
                        }
                        break;
                    case "upload":
                        status = "Shader subido: " + Remote.SaveShader(settings.ShaderFolder, c["name"]!.GetValue<string>(), c["text"]!.GetValue<string>());
                        ReloadShaders();
                        break;
                    case "new": NewProject(); break;
                    case "duplicate": DuplicateProject(); break;
                }
            }
            catch (Exception e) { status = "Control remoto: " + e.Message; }
        }
        renderer.StrobeHeld = remoteClock < strobeUntil;
        if (remoteClock < stateDue) return;
        stateDue = remoteClock + 0.2;
        var scenes = project.Scenes;
        var isf = player.Current >= 0 && player.Current < scenes.Count ? renderer.Get(scenes[player.Current].Shader).Isf : null;
        remote.State = Remote.State(player, isf, loadedName, projectNames, shaderFiles, analyzer.Bands,
            settings.AllowFastStrobe ? 20 : EffectDefs.SafeStrobeHz, renderer.StrobeActive);
    }

    void RemoteSettings()
    {
        ImGui.Separator();
        bool on = settings.RemoteEnabled;
        if (ImGui.Checkbox("Control remoto por Wi-Fi", ref on))
        {
            settings.RemoteEnabled = on;
            if (on) StartRemote(); else remote.Stop();
            settings.Save();
        }
        if (!on) return;
        ImGui.Indent();
        int port = settings.RemotePort;
        if (ImGui.InputInt("Puerto", ref port, 0, 0, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            settings.RemotePort = Math.Clamp(port, 1024, 65535);
            StartRemote();
        }
        if (remote.Error != null) Colored(Red, "No se pudo abrir el puerto: " + remote.Error);
        else
        {
            ImGui.TextWrapped("Escaneá el código con el teléfono (tiene que estar en la misma red Wi-Fi):");
            DrawQr(remoteUrl, 240);
            string url = remoteUrl;
            ImGui.InputText("##url", ref url, 256, ImGuiInputTextFlags.ReadOnly);
            if (ImGui.Button("Nuevo código")) { StartRemote(newToken: true); settings.Save(); }
            ImGui.SameLine();
            ImGui.TextDisabled("los teléfonos conectados dejan de funcionar");
            ImGui.TextDisabled("La primera vez Windows pregunta si permite la conexión: aceptá en redes privadas.");
        }
        ImGui.Unindent();
    }

    void DrawQr(string text, float size)
    {
        if (qrFor != text)
        {
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            qr = data.ModuleMatrix; // includes the white quiet zone
            qrFor = text;
        }
        int n = qr.Count;
        float cell = MathF.Max(1, MathF.Floor(size / n));
        var p = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, p + new Vector2(n * cell), 0xFFFFFFFF);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                if (qr[y][x]) dl.AddRectFilled(p + new Vector2(x * cell, y * cell), p + new Vector2((x + 1) * cell, (y + 1) * cell), 0xFF000000);
        ImGui.Dummy(new Vector2(n * cell));
    }
}
