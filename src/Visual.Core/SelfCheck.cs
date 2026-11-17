namespace Visual.Core;

/// <summary>Smallest checks for the non-trivial logic. Run: Visual.Desktop.exe --check (exit code 0 = ok).</summary>
public static class SelfCheck
{
    public static void Run()
    {
        Eq(IsfShader.ExpandMacros("IMG_PIXEL(img, vec2(f(1,2), 3.0)) + IMG_THIS_PIXEL(a)"),
            "texture2D(img, (vec2(f(1,2), 3.0)) / _img_imgSize) + texture2D(a, isf_FragNormCoord)");
        Eq(IsfShader.ExpandMacros("IMG_NORM_PIXEL(a, IMG_SIZE(b))"), "texture2D(a, _b_imgSize)");

        var s = IsfShader.Parse("""
            /*{ "INPUTS": [
                {"NAME":"c","TYPE":"color","DEFAULT":[1,0,0]},
                {"NAME":"m","TYPE":"long","LABELS":["a","b"]},
                {"NAME":"x","TYPE":"nonsense"},
            ]}*/
            void main(){}
            """);
        Check(s.Inputs.Count == 2, "parse count");
        Check(s.Inputs[0].Default.SequenceEqual([1f, 0, 0, 1]), "color default + alpha");
        Check(s.Inputs[1].Values.SequenceEqual([0, 1]), "long values from labels");
        Check(IsfShader.Parse("/*{\"DESCRIPTION\":\"maquínico\"}*/ void main(){} // ñ").BuildFragment("").All(ch => ch < 128), "GLSL sin acentos");

        var bands = new AudioBands();
        var pp = new Modifier { Kind = ModKind.Linear, Speed = 1, Mode = LinearMode.PingPong };
        Near(pp.Apply(0, 0, 10, 0.5, bands), 5);  // Speed = range sweeps per second
        Near(pp.Apply(0, 0, 10, 1.0, bands), 10);
        Near(pp.Apply(0, 0, 10, 1.5, bands), 5);   // coming back
        Near(pp.Apply(0, 0, 10, 2.0, bands), 0);
        Near(new Modifier { Kind = ModKind.Linear, Speed = 1 }.Apply(0, 0, 10, 0.25, bands), 2.5f);
        bands.Raw[0] = 0.5f;
        Near(new Modifier { Kind = ModKind.Audio, Amount = 1, Gain = 2 }.Apply(0, 0, 10, 0, bands), 10);
        Near(new Modifier { Kind = ModKind.Audio, Amount = 1, Threshold = 0.5f }.Apply(0, 0, 10, 0, bands), 0);

        AutoStrobeCheck();
        EffectsCheck();
        RemoteCheck();

        Check(BandOf(100) == 0, "100 Hz -> graves");
        Check(BandOf(1000) == 1, "1 kHz -> medios");
        Check(BandOf(8000) == 2, "8 kHz -> agudos");
    }

    static void AutoStrobeCheck()
    {
        var c = new AutoStrobe { Enabled = true, Probability = 100, Duration = 1, Cooldown = 5, Threshold = 0.5f };
        var r = new AutoStrobeRunner(new Random(1));
        var b = new AudioBands();
        bool Step(float level, float dt = 0.1f) { b.Raw[0] = level; return r.Update(c, b, dt, 3); }

        Step(0.9f);
        Check(r.Active, "autostrobe: arranca al cruzar el umbral");
        Step(0);
        Step(0);
        Step(0);
        Step(0.9f); // hit 0.4 s after the start: allowed at 3 Hz
        Check(r.SinceHit == 0, "autostrobe: golpe aceptado");
        Step(0);
        Step(0.9f); // 0.2 s after the previous hit: too fast
        Check(r.SinceHit > 0, "autostrobe: golpe demasiado rápido ignorado");
        bool ended = false;
        for (int i = 0; i < 6; i++) ended |= Step(0);
        Check(ended && !r.Active, "autostrobe: termina a la duración");
        Step(0.9f);
        Check(!r.Active, "autostrobe: respeta el enfriamiento");

        c.Probability = 0;
        var r2 = new AutoStrobeRunner(new Random(1));
        b.Raw[0] = 0.9f;
        r2.Update(c, b, 0.1f, 3);
        Check(!r2.Active, "autostrobe: probabilidad 0");

        // the scene's own strobe blocks bursts and cuts a running one without "ending" it (no scene change)
        c.Probability = 100;
        var r3 = new AutoStrobeRunner(new Random(1));
        b.Raw[0] = 0.9f;
        r3.Update(c, b, 0.1f, 3, blocked: true);
        Check(!r3.Active, "autostrobe: bloqueado por el strobe de la escena");
        b.Raw[0] = 0;
        r3.Update(c, b, 0.1f, 3);
        b.Raw[0] = 0.9f;
        r3.Update(c, b, 0.1f, 3);
        Check(r3.Active, "autostrobe: arranca sin bloqueo");
        Check(!r3.Update(c, b, 0.1f, 3, blocked: true) && !r3.Active, "autostrobe: se corta sin pasar de escena");
    }

    static void EffectsCheck()
    {
        // projects saved with global effects: each scene gets its own copy
        var p = new Project
        {
            Scenes = [new Scene(), new Scene()],
            Effects = new() { ["zoom"] = new ParamState { Value = [2] } },
        };
        p.EnsureEffects();
        Check(p.Effects == null && p.Scenes.All(s => s.Effects["zoom"].Value[0] == 2), "efectos: migración a escenas");
        p.Scenes[0].Effects["zoom"].Value[0] = 3;
        Check(p.Scenes[1].Effects["zoom"].Value[0] == 2, "efectos: copias independientes");
        p.Scenes[0].Effects["strobeDuty"].Value[0] = 0.5f;
        p.Scenes[0].Effects["tintHue"].Value[0] = 0.5f;
        Check(EffectDefs.ActiveLabels(p.Scenes[0].Effects).SequenceEqual(["Zoom"]), "efectos: activos (duración del strobe o tono solos no cuentan)");
        p.Scenes[0].Effects["opacity"].Value[0] = 0;
        Check(EffectDefs.ActiveLabels(p.Scenes[0].Effects).Contains("Opacidad"), "efectos: opacidad activa");
        p.Scenes[0].Effects["opacity"].Value[0] = 1;
        p.Scenes[0].Effects["hue"].Mod.Kind = ModKind.Linear;
        p.Scenes[0].Effects["strobeRate"].Value[0] = 2;
        Check(EffectDefs.ActiveLabels(p.Scenes[0].Effects).SequenceEqual(["Hue", "Zoom", "Strobe"]), "efectos: activos con modificador y strobe");
        p.Scenes[0].Effects["hue"].Off = true;
        Check(EffectDefs.ActiveLabels(p.Scenes[0].Effects).SequenceEqual(["Zoom", "Strobe"]), "efectos: apagado no cuenta");
        Check(p.Scenes[0].Effects["hue"].Clone() is { Off: true, Mod.Kind: ModKind.Linear }, "efectos: apagado conserva la configuración");
        p.CopyEffects(p.Scenes[0]);
        Check(p.Scenes[1].Effects["zoom"].Value[0] == 3 && p.Scenes[1].Effects["zoom"] != p.Scenes[0].Effects["zoom"], "efectos: copiar a todas");
    }

    static void RemoteCheck()
    {
        var pl = new Player();
        pl.Start(new Project { Scenes = [new Scene { Shader = "a.fs", Params = { ["x"] = new ParamState() } }] });
        System.Text.Json.Nodes.JsonObject Cmd(string json) => (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(json)!;

        Remote.Apply(Cmd("""{"op":"param","scene":0,"name":"x","state":{"Value":[0.5],"Mod":{"Kind":"Audio"}}}"""), pl, ["a.fs"]);
        var x = pl.Project.Scenes[0].Params["x"];
        Check(x.Value[0] == 0.5f && x.Mod.Kind == ModKind.Audio, "remoto: parámetro");
        Remote.Apply(Cmd("""{"op":"param","scene":0,"name":"x","state":{"Value":[1,2]}}"""), pl, ["a.fs"]);
        Check(pl.Project.Scenes[0].Params["x"].Value.Length == 1, "remoto: rechaza tamaño distinto");
        Remote.Apply(Cmd("""{"op":"add","shader":"../otro.fs"}"""), pl, ["a.fs"]);
        Check(pl.Project.Scenes.Count == 1, "remoto: solo shaders de la carpeta");
        Check(Remote.Apply(Cmd("""{"op":"save"}"""), pl, []) == "save", "remoto: comando del host");
        pl.Project.EnsureEffects();
        Remote.Apply(Cmd("""{"op":"effect","scene":0,"name":"hue","state":{"Value":[0.25]}}"""), pl, []);
        Check(pl.Project.Scenes[0].Effects["hue"].Value[0] == 0.25f, "remoto: efecto de la escena");

        using var srv = new RemoteServer();
        srv.Start(0, "secreto", loopbackOnly: true);
        Check(srv.Error == null, "remoto: abre el puerto");
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{srv.Port}") };
        Check(http.GetAsync("/state?k=otro").Result.StatusCode == System.Net.HttpStatusCode.Forbidden, "remoto: código inválido");
        Check(http.GetStringAsync("/?k=secreto").Result.Contains("<html"), "remoto: página");
        http.PostAsync("/cmd?k=secreto", new StringContent("""{"op":"next"}""")).Result.EnsureSuccessStatusCode();
        Check(srv.Commands.TryDequeue(out var c) && c["op"]!.GetValue<string>() == "next", "remoto: comando recibido");
        http.PostAsync("/upload?k=secreto&name=x.fs", new StringContent("void main(){}")).Result.EnsureSuccessStatusCode();
        Check(srv.Commands.TryDequeue(out var u) && u["name"]!.GetValue<string>() == "x.fs" && u["text"]!.GetValue<string>() == "void main(){}", "remoto: subida recibida");

        var dir = Path.Combine(Path.GetTempPath(), "visual-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            Check(Remote.SaveShader(dir, "a.fs", "void main(){}") == "a.fs" && File.Exists(Path.Combine(dir, "a.fs")), "subida: guarda");
            foreach (var bad in new[] { "../a.fs", "..\\a.fs", "a.txt", ".fs", "c:a.fs" })
            {
                bool rejected = false;
                try { Remote.SaveShader(dir, bad, "void main(){}"); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "subida: rechaza " + bad);
            }
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    static int BandOf(float hz)
    {
        var a = new AudioAnalyzer { SampleRate = 48000 };
        var buf = new float[4096];
        for (int i = 0; i < buf.Length; i++) buf[i] = 0.5f * MathF.Sin(2 * MathF.PI * hz * i / 48000);
        a.Push(buf);
        for (int i = 0; i < 10; i++) a.Update(1 / 60f);
        var r = a.Bands.Raw;
        return Array.IndexOf(r, r.Max());
    }

    static void Check(bool ok, string what) { if (!ok) throw new Exception("FALLO: " + what); }
    static void Eq(string a, string b) => Check(a == b, $"'{a}' != '{b}'");
    static void Near(float a, float b) => Check(MathF.Abs(a - b) < 1e-3f, $"{a} != {b}");
}
