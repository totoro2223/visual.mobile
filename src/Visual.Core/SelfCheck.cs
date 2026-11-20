namespace Visual.Core;

/// <summary>Smallest checks for the non-trivial logic. Run: Visual.Desktop.exe --check (exit code 0 = ok).</summary>
public static class SelfCheck
{
    public static void Run()
    {
        Eq(IsfShader.ExpandMacros("IMG_PIXEL(img, vec2(f(1,2), 3.0)) + IMG_THIS_PIXEL(a)"),
            "_img_px((vec2(f(1,2), 3.0)) / _img_imgSize) + _a_px(isf_FragNormCoord)");
        Eq(IsfShader.ExpandMacros("IMG_NORM_PIXEL(a, IMG_SIZE(b))"), "_a_px(_b_imgSize)");

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
        Check(IsfShader.Parse("// credits\n/* by me */\n/*{\"INPUTS\":[{\"NAME\":\"x\",\"TYPE\":\"float\"}]}*/ void main(){}").Inputs.Count == 1, "ISF: encabezado después de comentarios");
        var mp = IsfShader.Parse("""/*{"PERSISTENT_BUFFERS":["last"],"PASSES":[{"TARGET":"a"},{"TARGET":"x;y"},{"TARGET":"last"},{}]}*/ void main(){}""");
        Check(mp.Passes.SequenceEqual(["a", "", "last", ""]) && mp.Buffers.SequenceEqual(["a", "last"])
            && mp.BuildFragment("").Contains("uniform sampler2D last;") && mp.BuildVertex("") == null, "ISF: pasadas y buffers");
        mp.VertexSource = "varying vec2 c; void main(){ vv_vertShaderInit(); c = vv_FragNormCoord * RENDERSIZE; }";
        var vsrc = mp.BuildVertex(Renderer.EsHeader)!;
        Check(vsrc.StartsWith("precision highp float;\nprecision mediump int;\n") && vsrc.Contains("void isf_vertShaderInit()") && vsrc.Contains("uniform vec2 RENDERSIZE;"),
            "ISF: shader de vértices (precisión antes de los uniforms)");

        var bands = new AudioBands();
        // ease In: Rebotar plays it backwards (1.5 s = 0.5 back = 1.25); Rebotar invertido comes down with the same shape (10 - 1.25)
        Near(new Modifier { Kind = ModKind.Linear, Speed = 1, Ease = Ease.In, Mode = LinearMode.PingPong }.Apply(0, 0, 10, 1.5, new AudioBands()), 1.25f);
        var mi = new Modifier { Kind = ModKind.Linear, Speed = 1, Ease = Ease.In, Mode = LinearMode.Mirror };
        Near(mi.Apply(0, 0, 10, 0.5, new AudioBands()), 1.25f);
        Near(mi.Apply(0, 0, 10, 1.5, new AudioBands()), 8.75f);
        Near(mi.Apply(0, 0, 10, 2.5, new AudioBands()), 1.25f);
        Check(MathF.Abs(mi.Apply(0, 0, 10, 0.9999, new AudioBands()) - mi.Apply(0, 0, 10, 1.0001, new AudioBands())) < 0.01f, "rebotar invertido: sin salto arriba");
        var pp = new Modifier { Kind = ModKind.Linear, Speed = 1, Mode = LinearMode.PingPong };
        Near(pp.Apply(0, 0, 10, 0.5, bands), 5);  // Speed = range sweeps per second
        Near(pp.Apply(0, 0, 10, 1.0, bands), 10);
        Near(pp.Apply(0, 0, 10, 1.5, bands), 5);   // coming back
        Near(pp.Apply(0, 0, 10, 2.0, bands), 0);
        Near(new Modifier { Kind = ModKind.Linear, Speed = 1 }.Apply(0, 0, 10, 0.25, bands), 2.5f);
        bands.Raw[0] = 0.5f;
        Near(new Modifier { Kind = ModKind.Audio, Amount = 1, Gain = 2 }.Apply(0, 0, 10, 0, bands), 10);
        Near(new Modifier { Kind = ModKind.Audio, Amount = 1, Threshold = 0.5f }.Apply(0, 0, 10, 0, bands), 0);
        // range and invert
        Near(new Modifier { From = 0.2f, To = 0.6f }.Apply(9, 0, 10, 0, bands), 6);                     // slider value kept inside
        var rl = new Modifier { Kind = ModKind.Linear, Speed = 1, From = 0.2f, To = 0.6f };
        Near(rl.Apply(2, 0, 10, 0.5, bands), 4);                                                        // sweeps 2..6 only
        rl.Invert = true;
        Near(rl.Apply(3, 0, 10, 0, bands), 3);                                                          // starts at the slider value
        Near(rl.Apply(3, 0, 10, 0.5, bands), 5);                                                        // then goes down: 3 -> 2, wraps to 6 -> 5
        Near(new Modifier { Kind = ModKind.Audio, Amount = 1, Gain = 2, Invert = true }.Apply(10, 0, 10, 0, bands), 0); // louder = lower
        Near(new Modifier { Kind = ModKind.Audio, Amount = 1, Gain = 2, To = 0.5f }.Apply(0, 0, 10, 0, bands), 5);
        // audio: From = 0 %, To = 100 %, Intensity scales inside; the slider value doesn't matter
        Near(new Modifier { Kind = ModKind.Audio, Amount = 1, Gain = 2, From = 0.5f, To = 0.6f }.Apply(9, 0, 10, 0, bands), 6);
        Near(new Modifier { Kind = ModKind.Audio, Amount = 0.5f, Gain = 2, From = 0.5f, To = 0.6f }.Apply(0, 0, 10, 0, bands), 5.5f);
        Near(new Modifier { Kind = ModKind.Audio, Amount = 1, Threshold = 0.9f, From = 0.5f, To = 0.6f }.Apply(9, 0, 10, 0, bands), 5); // silence rests at From
        Near(new Modifier { Kind = ModKind.Audio, Amount = -1, Threshold = 0.9f, From = 0.5f, To = 0.6f }.Apply(0, 0, 10, 0, bands), 6); // negative rests at To
        Near(new Modifier { Kind = ModKind.Audio, Amount = -0.5f, Gain = 2, From = 0.5f, To = 0.6f }.Apply(0, 0, 10, 0, bands), 5.5f);
        CurvesCheck(bands);

        AutoStrobeCheck();
        EffectsCheck();
        RemoteCheck();
        NetAudioCheck();
        MusicCheck();

        Check(Renderer.InternalSize(1920, 1080, 0.5f) == (960, 540) && Renderer.InternalSize(1920, 1080, 9) == (1920, 1080)
              && Renderer.InternalSize(1, 1, 0.35f) == (1, 1) && Renderer.InternalSize(800, 600, float.NaN) == (800, 600), "resolución interna");
        Check(Renderer.Fit("full", 5, 5, 5, 5, 1920, 1080) == (0, 0, 1920, 1080), "lienzo completo");
        Check(Renderer.Fit("4:3", 0, 0, 0, 0, 1920, 1080) == (240, 0, 1440, 1080), "lienzo 4:3 centrado");
        Check(Renderer.Fit("16:9", 0, 0, 0, 0, 1080, 1920) == (0, 656, 1080, 608), "lienzo 16:9 en pantalla vertical");
        Check(Renderer.Fit("px", 1900, -5, 500, 500, 1920, 1080) == (1900, 0, 20, 500), "lienzo en píxeles recortado");
        Check(Renderer.Fit("x:0", 0, 0, 0, 0, 800, 600) == (0, 0, 800, 600), "formato inválido -> completo");
        var cpl = new Player();
        cpl.Start(new Project { Crossfade = true, CrossfadeSeconds = 2, Scenes = [new Scene(), new Scene { Canvas = new SceneCanvas { Mode = "4:3" } }] });
        var half = (0f, 0f, 0.5f, 1f);
        Check(Renderer.Place(cpl, half, 1920, 1080).Shown == half, "escena sin lienzo propio usa el global");
        cpl.GoTo(1);
        cpl.Update(1); // mitad del fundido
        var (cc, cs) = Renderer.Place(cpl, half, 1920, 1080);
        Check(cc == (0.125f, 0f, 0.75f, 1f), "lienzo propio 4:3");
        Check(MathF.Abs(cs.X - 0.0625f) < 1e-5f && MathF.Abs(cs.W - 0.625f) < 1e-5f, "el recuadro se desliza durante el fundido");

        Check(BandOf(100) == 0, "100 Hz -> graves");
        Check(BandOf(1000) == 1, "1 kHz -> medios");
        Check(BandOf(8000) == 2, "8 kHz -> agudos");
        Check(BandOf(1800) == 1 && BandOf(2500) == 2, "corte medios/agudos en 2 kHz");
        AnalyzerCheck();
    }

    static void CurvesCheck(AudioBands bands)
    {
        Check(Curves.Names.Length == Enum.GetValues<Ease>().Length, "curvas: un nombre por curva");
        foreach (var e in Enum.GetValues<Ease>())
        {
            var m = new Modifier { Kind = ModKind.Linear, Speed = 0.37f, Ease = e, From = 0.2f, To = 0.7f };
            foreach (var mode in Enum.GetValues<LinearMode>())
                for (int i = 0; i <= 200; i++)
                {
                    m.Mode = mode;
                    float v = m.Apply(4, 0, 10, i * 0.05, bands);
                    Check(v >= 2 - 1e-4f && v <= 7 + 1e-4f, $"curva {e} {mode}: dentro de Desde-Hasta ({v})");
                }
            m.Mode = LinearMode.Loop;
            if (Curves.Rising(e))
                foreach (bool inv in new[] { false, true })
                    foreach (float start in new[] { 2f, 3.3f, 5f, 6.9f })
                    {
                        m.Invert = inv;
                        float v0 = m.Apply(start, 0, 10, 0, bands);
                        Check(e == Ease.Steps ? MathF.Abs(v0 - start) <= 5f / 3 + 1e-3f : MathF.Abs(v0 - start) < 1e-3f, $"curva {e}: arranca en el slider ({start} -> {v0})");
                    }
            if (e is not (Ease.Wave or Ease.RandomSteps or Ease.RandomSmooth or Ease.Bounce or Ease.Back or Ease.Elastic))
                Check(Math.Abs(Curves.Of(e, 4, 0)) < 1e-9 && Math.Abs(Curves.Of(e, 4, 1) - 1) < 1e-9, $"curva {e}: de 0 a 1");
        }
        var st = new Modifier { Kind = ModKind.Linear, Speed = 1, Ease = Ease.Steps, Steps = 4 };
        Check(Enumerable.Range(0, 100).Select(i => st.Apply(0, 0, 9, i * 0.01, bands)).Distinct().Count() == 4, "escalones: 4 valores");
        Check(Math.Abs(Curves.Of(Ease.Back, 4, 0.5) - 0.5) < 1e-6 && Curves.Of(Ease.Elastic, 4, 1) < 1, "pasarse y elástico achicados");
    }

    static void AutoStrobeCheck()
    {
        var c = new AutoStrobe { Enabled = true, Probability = 100, Cooldown = 5, Threshold = 0.5f, Configs = [new StrobeConfig { Duration = 1 }] };
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

        // several configurations: in order, at random (never twice in a row, skipping the ones out of the rotation)
        var m = new AutoStrobe { Enabled = true, Probability = 100, Cooldown = 0, Threshold = 0.5f,
            Configs = [new() { Duration = 0.05f }, new() { Duration = 0.05f, NextScene = true }, new() { Duration = 0.05f }] };
        var rm = new AutoStrobeRunner(new Random(3));
        int Fire() { b.Raw[0] = 0; rm.Update(m, b, 0.1f, 3); b.Raw[0] = 0.9f; rm.Update(m, b, 0.1f, 3); return rm.Active ? rm.Current : -9; }
        Check(string.Join(",", Enumerable.Range(0, 4).Select(_ => Fire())) == "0,1,2,0", "autostrobe: configuraciones en orden");
        Fire();
        b.Raw[0] = 0;
        Check(rm.Update(m, b, 0.1f, 3) && rm.Ended(m)?.NextScene == true, "autostrobe: pasar de escena es por configuración");
        Check(rm.Flash(m) == m.Configs[rm.Upcoming(m)] && rm.Upcoming(m) == 2, "autostrobe: el botón usa la próxima");
        m.Shuffle = true;
        m.Configs.Add(new() { Duration = 0.05f, On = false });
        var seq = Enumerable.Range(0, 60).Select(_ => Fire()).ToList();
        Check(seq.Zip(seq.Skip(1)).All(z => z.First != z.Second) && seq.All(x => x is >= 0 and <= 2) && seq.Distinct().Count() == 3,
            "autostrobe: al azar sin repetir y salteando las inactivas");
        foreach (var x in m.Configs) x.On = false;
        b.Raw[0] = 0; rm.Update(m, b, 0.1f, 3); rm.Update(m, b, 0.1f, 3);
        b.Raw[0] = 0.9f;
        rm.Update(m, b, 0.1f, 3);
        Check(!rm.Active && rm.Upcoming(m) == -1, "autostrobe: sin configuraciones activas no dispara");

        // projects saved with one configuration; save / load keeps the list
        var op = ProjectStore.Parse("""{"Name":"v","Scenes":[],"AutoStrobe":{"Enabled":true,"Rate":2.5,"Rhythm":"Beat","NextScene":true,"Mode":3}}""").AutoStrobe;
        Check(op.Enabled && op.Configs.Count == 1 && op.Configs[0] is { Rate: 2.5f, Rhythm: StrobeRhythm.Beat, NextScene: true, Mode: 3 } && op.Old == null,
            "autostrobe: proyecto viejo -> una configuración");
        op.Configs.Add(new() { Name = "b", Mode = 1 });
        var back = ProjectStore.Parse(ProjectStore.ToJson(new Project { AutoStrobe = op })).AutoStrobe;
        Check(back.Configs.Count == 2 && back.Configs[1] is { Name: "b", Mode: 1 } && back.Configs[0].Rate == 2.5f, "autostrobe: guardar y cargar conserva las configuraciones");
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
        p.Scenes[1].Effects["maskX"].Value[0] = 0.2f;
        Check(EffectDefs.ActiveLabels(p.Scenes[1].Effects).SequenceEqual(["Zoom"]), "máscara: mover sin achicar no cuenta");
        p.Scenes[1].Effects["maskW"].Value[0] = 0.3f;
        p.Scenes[1].Effects["maskH"].Mod.Kind = ModKind.Audio;
        Check(EffectDefs.ActiveLabels(p.Scenes[1].Effects).SequenceEqual(["Zoom", "Máscara"]), "máscara: una sola etiqueta");
        Check(p.Scenes[0].Effects["hue"].Clone() is { Off: true, Mod.Kind: ModKind.Linear }, "efectos: apagado conserva la configuración");
        p.CopyEffects(p.Scenes[0]);
        Check(p.Scenes[1].Effects["zoom"].Value[0] == 3 && p.Scenes[1].Effects["zoom"] != p.Scenes[0].Effects["zoom"], "efectos: copiar a todas");
    }

    static void LayersCheck(Player pl, Func<string, System.Text.Json.Nodes.JsonObject> cmd)
    {
        var s = pl.Project.Scenes[0];
        string[] sh = ["a.fs", "b.fs"];
        Remote.Apply(cmd("""{"op":"layerAdd","scene":0,"shader":"b.fs"}"""), pl, sh);
        Remote.Apply(cmd("""{"op":"layerAdd","scene":0,"shader":"../x.fs"}"""), pl, sh);
        Check(s.Layers.Count == 1 && s.Layers[0].Effects.Count == EffectDefs.All.Length, "capas: agregar (con sus efectos, solo shaders de la carpeta)");
        Remote.Apply(cmd("""{"op":"effect","scene":0,"layer":0,"name":"hue","state":{"Value":[0.75]}}"""), pl, sh);
        Check(s.Layers[0].Effects["hue"].Value[0] == 0.75f && s.Effects["hue"].Value[0] == 0.25f, "capas: efecto de la capa no toca la base");
        Remote.Apply(cmd("""{"op":"layerSet","scene":0,"layer":0,"blend":4,"off":true}"""), pl, sh);
        Remote.Apply(cmd("""{"op":"layerSet","scene":0,"layer":0,"blend":99}"""), pl, sh);
        Check(s.Layers[0] is { Blend: BlendMode.Screen, Off: true }, "capas: blend y apagar");
        Remote.Apply(cmd("""{"op":"layerDup","scene":0,"layer":0}"""), pl, sh);
        Check(s.Layers.Count == 2 && s.Layers[1].Effects["hue"] != s.Layers[0].Effects["hue"] && s.Layers[1].Effects["hue"].Value[0] == 0.75f, "capas: duplicar es una copia");
        Remote.Apply(cmd("""{"op":"layerSet","scene":0,"layer":1,"shader":"a.fs","off":false}"""), pl, sh);
        Remote.Apply(cmd("""{"op":"layerMove","scene":0,"layer":1,"to":0}"""), pl, sh);
        Check(s.Layers[0].Shader == "a.fs" && s.Layers[1].Shader == "b.fs", "capas: orden");
        Remote.Apply(cmd("""{"op":"media","scene":0,"layer":0,"name":"inputImage","value":{"Src":"v.mp4"}}"""), pl, sh);
        Check(s.Layers[0].Media.ContainsKey("inputImage") && !s.Media.ContainsKey("inputImage"), "capas: video de la capa");
        var back = ProjectStore.Parse(ProjectStore.ToJson(pl.Project)).Scenes[0];
        Check(back.Layers.Count == 2 && back.Layers[1] is { Blend: BlendMode.Screen, Off: true } && back.Layers[1].Effects["hue"].Value[0] == 0.75f, "capas: guardar y cargar");
        Check(ProjectStore.Parse("""{"Name":"v","Scenes":[{"Shader":"a.fs","Name":"","Media":{},"Params":{},"Effects":{}}],"AutoStrobe":{}}""").Scenes[0].Layers.Count == 0, "capas: proyecto viejo sin capas");
        bool bad = false;
        try { ProjectStore.Parse("""{"Name":"v","Scenes":[{"Shader":"a.fs","Name":"","Media":{},"Params":{},"Effects":{},"Layers":[null]}],"AutoStrobe":{}}"""); }
        catch (InvalidDataException) { bad = true; }
        Check(bad, "capas: rechaza una capa vacía");

        // safety: strobes of the scene and its layers count together
        foreach (var l in s.Layers) { l.Off = false; l.Effects["strobeRate"].Value[0] = 3; }
        float total = Renderer.StrobeTotal(pl, 0, new AudioBands(), EffectDefs.SafeStrobeHz, 1);
        Check(total == 7, "capas: suma de strobes");
        s.Layers[1].Effects["strobeIntensity"].Value[0] = 0;
        Check(Renderer.StrobeTotal(pl, 0, new AudioBands(), EffectDefs.SafeStrobeHz, 0) == 3, "capas: strobe sin intensidad no cuenta");
        Remote.Apply(cmd("""{"op":"layerRemove","scene":0,"layer":0}"""), pl, sh);
        Remote.Apply(cmd("""{"op":"layerRemove","scene":0,"layer":5}"""), pl, sh);
        Check(s.Layers.Count == 1 && s.Layers[0].Shader == "b.fs", "capas: quitar");
        s.Layers.Clear();
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
        var mm = new MusicMeter();
        Remote.Apply(Cmd("""{"op":"music","bpm":124,"beat":2}"""), pl, [], mm);
        Check(mm.Bpm == 124 && mm.Locked && mm.Beat == 2, "remoto: tap del teléfono");
        Remote.Apply(Cmd("""{"op":"music","lock":false}"""), pl, [], mm);
        Check(!mm.Locked, "remoto: música en automático");
        Remote.Apply(Cmd("""{"op":"autodj","value":{"Enabled":false,"Bars":500,"Drops":false}}"""), pl, []);
        Check(pl.Project.AutoDj is { Enabled: false, Bars: 64, Drops: false }, "remoto: auto dj");
        Remote.Apply(Cmd("""{"op":"intensity","scene":0,"value":3}"""), pl, []);
        Check(pl.Project.Scenes[0].Intensity == 3, "remoto: intensidad de la escena");
        pl.Project.EnsureEffects();
        Remote.Apply(Cmd("""{"op":"effect","scene":0,"name":"hue","state":{"Value":[0.25]}}"""), pl, []);
        Check(pl.Project.Scenes[0].Effects["hue"].Value[0] == 0.25f, "remoto: efecto de la escena");
        LayersCheck(pl, Cmd);

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

        // scene name and video
        Remote.Apply(Cmd("""{"op":"name","scene":0,"value":"  Playa  "}"""), pl, []);
        Check(pl.Project.Scenes[0].Title == "Playa", "escena: nombre");
        Remote.Apply(Cmd("""{"op":"media","scene":0,"name":"inputImage","value":{"Src":"playa.mp4","Speed":9}}"""), pl, []);
        Check(pl.Project.Scenes[0].Media["inputImage"] is { Src: "playa.mp4", Speed: SceneMedia.MaxSpeed }, "video: archivo y velocidad limitada");
        foreach (var bad in new[] { "../x.mp4", "c:\\x.mp4", "x.exe", "ftp://h/x.mp4" })
        {
            Remote.Apply(Cmd("""{"op":"media","scene":0,"name":"inputImage","value":{"Src":""" + System.Text.Json.JsonSerializer.Serialize(bad) + "}}"), pl, []);
            Check(pl.Project.Scenes[0].Media["inputImage"].Src == "playa.mp4", "video: rechaza " + bad);
        }
        Remote.Apply(Cmd("""{"op":"media","scene":0,"name":"inputImage","value":{"Src":"https://h/v.mp4"}}"""), pl, []);
        Check(Media.Resolve("/m", "https://h/v.mp4") == "https://h/v.mp4" && Media.Resolve("/m", "https://") == null, "video: URL");
        Remote.Apply(Cmd("""{"op":"media","scene":0,"name":"inputImage","value":null}"""), pl, []);
        Check(pl.Project.Scenes[0].Media.Count == 0, "video: quitar");
        Check(IsfShader.Parse("""/*{"INPUTS":[{"NAME":"img","TYPE":"image"}]}*/ void main(){ gl_FragColor = IMG_THIS_PIXEL(img); }""")
            .BuildFragment("").Contains("vec4 _img_px(vec2 p)"), "imagen: lectura con giro");

        var dir = Path.Combine(Path.GetTempPath(), "visual-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            // big upload: written to disk, announced to the host
            srv.MediaFolder = dir;
            var body = new byte[700_000];
            Random.Shared.NextBytes(body);
            http.PostAsync("/media?k=secreto&name=v.mp4", new ByteArrayContent(body)).Result.EnsureSuccessStatusCode();
            Check(File.ReadAllBytes(Path.Combine(dir, "v.mp4")).SequenceEqual(body), "video: subida completa");
            Check(srv.Commands.TryDequeue(out var mc) && mc["op"]!.GetValue<string>() == "mediaChanged", "video: avisa al host");
            // a refused upload is answered without reading the body, so the client may just see the connection close
            bool refused;
            try { refused = !http.PostAsync("/media?k=secreto&name=..%2Fv.mp4", new ByteArrayContent(body)).Result.IsSuccessStatusCode; }
            catch (AggregateException) { refused = true; }
            Check(refused && !File.Exists(Path.Combine(Path.GetTempPath(), "v.mp4.part")), "video: rechaza ruta");
            Check(Media.List(dir).SequenceEqual(["v.mp4"]), "video: lista");

            Check(Remote.SaveShader(dir, "a.fs", "void main(){}") == "a.fs" && File.Exists(Path.Combine(dir, "a.fs")), "subida: guarda");
            foreach (var bad in new[] { "../a.fs", "..\\a.fs", "a.txt", ".fs", "c:a.fs" })
            {
                bool rejected = false;
                try { Remote.SaveShader(dir, bad, "void main(){}"); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "subida: rechaza " + bad);
            }

            // shader preview: GLSL ES + inputs for the browser; bad names and missing files are a 400
            File.WriteAllText(Path.Combine(dir, "b.fs"), """/*{"INPUTS":[{"NAME":"zoom","TYPE":"float","MAX":4}]}*/ void main(){ gl_FragColor = vec4(zoom); }""");
            srv.ShaderFolder = dir;
            Remote.SaveShader(dir, "b.vs", "void main(){ isf_vertShaderInit(); }"); // the .vs is uploaded the same way
            var pvr = http.GetAsync("/shaderSource?k=secreto&name=b.fs").Result;
            var pvj = System.Text.Json.Nodes.JsonNode.Parse(pvr.Content.ReadAsStringAsync().Result)!;
            Check(pvr.IsSuccessStatusCode && pvj["Frag"]!.GetValue<string>().StartsWith(Renderer.EsHeader)
                && pvj["Vert"]!.GetValue<string>().Contains("isf_vertShaderInit(); }") && pvj["Passes"]!.AsArray().Count == 1
                && pvj["Inputs"]![0]!["Name"]!.GetValue<string>() == "zoom" && pvj["Inputs"]![0]!["Max"]![0]!.GetValue<float>() == 4, "vista previa: fuente");
            foreach (var bad in new[] { "..%2Fb.fs", "nada.fs", "b.txt" })
                Check(http.GetAsync("/shaderSource?k=secreto&name=" + bad).Result.StatusCode == System.Net.HttpStatusCode.BadRequest, "vista previa: rechaza " + bad);

            // project JSON: download answered by the host thread, upload checked before it is queued
            var dl = http.GetAsync("/project?k=secreto");
            for (int t = 0; t < 100 && srv.ExportRequests.IsEmpty; t++) Thread.Sleep(10);
            srv.AnswerExports(pl.Project);
            var res = dl.Result;
            var got = res.Content.ReadAsStringAsync().Result;
            Check(res.IsSuccessStatusCode && ProjectStore.Parse(got).Scenes.Count == pl.Project.Scenes.Count
                  && res.Content.Headers.ContentDisposition?.FileNameStar == pl.Project.Name + ".json", "proyecto: descarga");
            Check(http.PostAsync("/importProject?k=secreto", new StringContent("""{"Scenes":[null]}""")).Result.StatusCode == System.Net.HttpStatusCode.BadRequest
                  && srv.Commands.IsEmpty, "proyecto: rechaza archivo inválido");
            http.PostAsync("/importProject?k=secreto", new StringContent(got)).Result.EnsureSuccessStatusCode();
            Check(srv.Commands.TryDequeue(out var ic) && ic["op"]!.GetValue<string>() == "import", "proyecto: subida al host");
            var st = new ProjectStore(dir);
            var first = st.Import(got);
            Check(first.Name == pl.Project.Name && st.Import(got).Name == pl.Project.Name + " 2" && st.List().Length == 2, "proyecto: importar sin pisar");
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
        var r = a.Bands.Raw[..3];
        return Array.IndexOf(r, r.Max());
    }

    /// <summary>Feeds <paramref name="frames"/> frames at 60 fps of a signal (sample index -> value) and returns the analyzer.</summary>
    static AudioAnalyzer Feed(Func<int, float> sig, int frames, AudioAnalyzer? a = null)
    {
        a ??= new AudioAnalyzer { SampleRate = 48000 };
        var buf = new float[800];
        for (int fr = 0, n = 0; fr < frames; fr++)
        {
            for (int i = 0; i < buf.Length; i++, n++) buf[i] = sig(n);
            a.Push(buf);
            a.Update(1 / 60f);
        }
        return a;
    }

    static void AnalyzerCheck()
    {
        // pink-like noise (equal-amplitude sines, evenly spaced per octave): the bands differ only by the music tilt
        var rnd = new Random(5);
        var pink = Enumerable.Range(0, 200).Select(i => (Hz: 20 * Math.Pow(2, i / 22.0), Ph: rnd.NextDouble() * 6.283)).Where(x => x.Hz < 10000).ToArray();
        var pa = Feed(n => (float)(0.02 * pink.Sum(x => Math.Sin(2 * Math.PI * x.Hz * n / 48000 + x.Ph))), 20);
        var r = pa.Bands.Raw;
        for (int b = 1; b < 3; b++)
        {
            float ratio = r[b] / r[0] / AudioAnalyzer.Tilt[b];
            Check(ratio is > 0.75f and < 1.33f, $"bandas: ruido rosa parejo ({BandNamesCheck[b]} {ratio:0.00})");
        }

        // a held note with harmonics: melody up, note = its pitch; 220 < 440 < 880
        float NoteOf(float hz, out float melody)
        {
            var a = Feed(n => (float)Enumerable.Range(1, 4).Sum(h => 0.3 / h * Math.Sin(2 * Math.PI * hz * h * n / 48000)), 120);
            melody = a.Bands.Raw[(int)Band.Melody];
            return a.Bands.Raw[(int)Band.Note];
        }
        float n440 = NoteOf(440, out float mel), n220 = NoteOf(220, out _), n880 = NoteOf(880, out _);
        float semis = 12 * MathF.Log2(AudioAnalyzer.NoteHi / AudioAnalyzer.NoteLo), want = 12 * MathF.Log2(440 / AudioAnalyzer.NoteLo) / semis;
        Check(MathF.Abs(n440 - want) < 1 / semis, $"nota: La 440 ({n440 * semis:0.0} semitonos, esperado {want * semis:0.0})");
        Check(MathF.Abs(n220 - (want - 12 / semis)) < 1 / semis && MathF.Abs(n880 - (want + 12 / semis)) < 1 / semis, $"nota: octavas ({n220 * semis:0.0} / {n880 * semis:0.0})");
        Check(mel > 0.2f, $"melodía: sube con una nota sostenida ({mel:0.00})");

        // drum hits (short noise bursts, as loud as the note): the melody barely moves
        var nr = new Random(9);
        float peakMel = 0;
        var da = new AudioAnalyzer { SampleRate = 48000 };
        for (int hit = 0; hit < 6; hit++)
        {
            Feed(n => n < 600 ? (float)(nr.NextDouble() * 2 - 1) * 0.6f : 0, 1, da);
            for (int i = 0; i < 14; i++) { Feed(_ => 0, 1, da); peakMel = MathF.Max(peakMel, da.Bands.Raw[(int)Band.Melody]); }
        }
        // a note cut on and off 4 times a second: the melody averages it instead of jumping
        var ca = new AudioAnalyzer { SampleRate = 48000 };
        float lo = float.MaxValue, hi = 0;
        for (int fr = 0; fr < 240; fr++)
        {
            Feed(n => (fr / 8) % 2 == 0 ? (float)Enumerable.Range(1, 4).Sum(h => 0.3 / h * Math.Sin(2 * Math.PI * 440 * h * (n + fr * 800) / 48000)) : 0, 1, ca);
            if (fr >= 120) { float m = ca.Bands.Raw[(int)Band.Melody]; lo = MathF.Min(lo, m); hi = MathF.Max(hi, m); }
        }
        Check(hi - lo < hi * 0.5f, $"melodía: se mueve suave ({lo:0.00}..{hi:0.00})");

        Check(peakMel < mel * 0.3f, $"melodía: ignora los golpes ({peakMel:0.00} vs nota {mel:0.00})");

        var sa = Feed(n => 0.3f * MathF.Sin(2 * MathF.PI * 1000 * n / 48000), 20);
        float mid = sa.Bands.Raw[1];
        sa.Sensitivity = [1, 2, 1, 1];
        Feed(n => 0.3f * MathF.Sin(2 * MathF.PI * 1000 * (n + 16000) / 48000), 20, sa);
        Check(MathF.Abs(sa.Bands.Raw[1] / mid - 2) < 0.1f, "bandas: sensibilidad x2");
    }

    static readonly string[] BandNamesCheck = ["graves", "medios", "agudos"];

    static void NetAudioCheck()
    {
        var pk = new byte[2048];
        int len = NetAudio.Encode(pk, "abc123", 44100, [0f, 0.5f, -1f, 2f]);
        var o = new float[NetAudio.MaxSamples];
        Check(NetAudio.Decode(pk.AsSpan(0, len), "abc123", o, out int rate) == 4 && rate == 44100, "audio red: decodifica");
        Check(MathF.Abs(o[1] - 0.5f) < 1e-3f && o[2] < -0.99f && o[3] > 0.99f, "audio red: valores");
        Check(NetAudio.Decode(pk.AsSpan(0, len), "abc124", o, out _) == -1, "audio red: código equivocado");
        Check(NetAudio.Decode(pk.AsSpan(0, 5), "abc123", o, out _) == -1, "audio red: paquete corto");
        Check(NetAudio.ParseUrl("http://192.168.1.5:8765/?k=ff00") == ("192.168.1.5", 8765, "ff00"), "audio red: url");
        Check(NetAudio.ParseUrl("192.168.1.5") == null && NetAudio.ParseUrl("http://x:1/") == null, "audio red: url mala");

        // end to end over loopback: sender's packets reach the analyzer
        var an = new AudioAnalyzer();
        int port = 40000 + Random.Shared.Next(20000);
        using var rx = new NetAudioReceiver(port, "tok", an);
        using var tx = new NetAudioSender($"http://127.0.0.1:{port}/?k=tok");
        using (var bad = new NetAudioSender($"http://127.0.0.1:{port}/?k=nop")) bad.Push(new float[2000], 48000);
        tx.Push(new float[2000], 96000); // 96 kHz -> averaged to 48 kHz: 1000 samples = 1 full packet
        for (int i = 0; i < 100 && rx.Packets == 0; i++) Thread.Sleep(10);
        Check(rx.Packets == 1 && an.SampleRate == 48000, $"audio red: llegó {rx.Packets} paquete(s) a {an.SampleRate} Hz");
    }

    static void MusicCheck()
    {
        // the whole path: synthetic audio -> analyzer (FFT, onsets) -> tempo, on a fast PC and on a slow TV
        (string Name, double Bpm, Func<double, Random, float> Sound)[] songs =
        [
            ("bombo", 128, (t, _) => Kick(t, 60 / 128.0)),
            ("bombo y hi-hats en corcheas", 90, (t, n) => Kick(t, 60 / 90.0) + Hat(t, 30 / 90.0, n)), // 90 must not read as 180
            ("808 sostenido con clap", 140, (t, _) => 0.5f * MathF.Sin(2 * MathF.PI * 45 * (float)t) + Clap(t, 60 / 140.0)),
            ("solo hi-hats", 100, (t, n) => Hat(t, 0.6, n)),
            ("batería en vivo (±18 ms)", 120, (t, _) => Kick(Loose(t, 0.5), 0.5)),
        ];
        foreach (var fps in new[] { 60, 30 })
            foreach (var (name, bpm, sound) in songs)
            {
                var s = new Synth(fps);
                s.Play(12, sound);
                var m = s.A.Music;
                Check(Math.Abs(m.Bpm - bpm) < 1.5 && m.Confidence >= 0.5, $"música a {fps} fps: {name} a {bpm} BPM (midió {m.Bpm:0.0}, confianza {m.Confidence:0.00})");
                if (name != "bombo") continue;
                Check(m.Energy > 50 && !m.Silence, $"música: energía con bombo ({m.Energy:0})");
                // the lights: right after a kick the beat has just started
                double beat = 60 / bpm, since = s.T % beat / beat, off = Math.Abs(m.BarPhase % 1 - since);
                Check(Math.Min(off, 1 - off) < 0.15, $"música a {fps} fps: el pulso cae en el bombo (fase {m.BarPhase % 1:0.00}, bombo {since:0.00})");
            }

        var q = new Synth(60);
        q.Play(12, (t, _) => Kick(t, 0.5));
        q.Play(5, (_, _) => 0);
        Check(q.A.Music.Silence && q.A.Music.Energy == 0 && q.A.Music.Bpm > 0, "música: silencio, guarda el último BPM");
        var noise = new Synth(60);
        noise.Play(10, (_, n) => 0.3f * (n.NextSingle() * 2 - 1));
        Check(noise.A.Music.Confidence < MusicMeter.MinConfidence, $"música: ruido sin ritmo (confianza {noise.A.Music.Confidence:0.00})");

        var b = new AudioBands();
        var tap = new MusicMeter();
        for (int i = 0; i < 4; i++) { tap.Update(b, 0, 0.5f); tap.Tap(); }
        Check(Math.Abs(tap.Bpm - 120) < 0.01 && tap.Locked && tap.Beat == 3 && tap.TapCount == 4, $"música: tap 120 BPM ({tap.Bpm})");
        tap.Unlock();
        Check(!tap.Locked, "música: volver a automático");
        AutoDjCheck();
    }

    /// <summary>A set at 128 BPM: loud part, break, drop, silence; the scenes have fixed intensities and 5 s durations.</summary>
    static void AutoDjCheck()
    {
        var p = new Project { AutoDj = new AutoDj { Bars = 8 } };
        foreach (var lv in new[] { 1, 1, 2, 3, 3 }) p.Scenes.Add(new Scene { Shader = "x.fs", Intensity = lv, Duration = 5 });
        var pl = new Player();
        pl.Start(p);
        var s = new Synth(60);
        var m = s.A.Music;
        var dj = new AutoDjRunner(new Random(1));
        const float dt = 1 / 60f;
        double beat = 60 / 128.0;
        int changes = 0;
        string bad = "";
        void Run(double seconds, bool kicks, Action<int>? onChange = null)
        {
            double start = s.T;
            s.Play(seconds, (t, _) => (kicks ? Kick(t - start, beat) : 0) + 0.3f * MathF.Sin(2 * MathF.PI * 440 * (float)t), () =>
            {
                int was = pl.Current;
                pl.Update(dt);
                dj.Update(p.AutoDj, pl, m, dt);
                if (pl.Current != was) onChange?.Invoke(was);
            });
        }

        Run(40, true, was =>
        {
            if (!dj.Active) return; // first seconds: no beat yet, the durations work
            changes++;
            if (m.BarPhase > 0.2 || p.Scenes[pl.Current].Level != 3) bad += $" t={s.T:0.0} fase={m.BarPhase:0.00} nivel={p.Scenes[pl.Current].Level} ({dj.LastChange});";
        });
        Check(dj.Active && pl.AutoDriving, "auto dj: activo con ritmo claro");
        Check(bad == "", "auto dj: cambia en el 1, a escenas fuertes:" + bad);
        Check(changes is >= 1 and <= 3, $"auto dj: cada 8 compases, no por duración ({changes} cambios en 40 s)");

        Run(12, false);
        Check(p.Scenes[pl.Current].Level == 1 && dj.Active, $"auto dj: corte -> escena suave (nivel {p.Scenes[pl.Current].Level}, {dj.LastChange})");

        int before = pl.Current;
        double cutAt = -1, phaseAtCut = -1, dropStart = s.T;
        Run(1, true, _ => { cutAt = s.T - dropStart; phaseAtCut = m.BarPhase; });
        Check(pl.Current != before && p.Scenes[pl.Current].Level == 3 && dj.LastChange.StartsWith("drop") && cutAt < 0.5,
            $"auto dj: drop -> fuerte enseguida ({dj.LastChange}, a los {cutAt:0.00} s)");
        // the "1" goes where the drop started: at the cut, the bar is as far along as the time since then
        Check(Math.Abs(phaseAtCut - cutAt / beat) < 0.3, $"auto dj: el drop marca el 1 (fase {phaseAtCut:0.00}, esperada {cutAt / beat:0.00})");

        s.Play(5, (_, _) => 0, () => dj.Update(p.AutoDj, pl, m, dt));
        Check(!dj.Active && !pl.AutoDriving, "auto dj: en silencio vuelven las duraciones");
        p.AutoDj.Enabled = false;
        Check(dj.Status(p.AutoDj, p) == "Apagado", "auto dj: apagado");

        var probe = new byte[8];
        Check(Renderer.Difference(probe, [255, 255, 255, 0, 0, 0, 0, 9]) == 0.5f, "movimiento: diferencia de color");
        Check(new Scene { Motion = 0.01f }.Level == 1 && new Scene { Motion = 0.2f }.Level == 3 && new Scene().Level == 2
              && new Scene { Motion = 0.2f, Intensity = 1 }.Level == 1, "intensidad: medida o elegida");
    }

    // ---- synthetic music ----
    /// <summary>Kick drum: a 55 Hz thump on every beat.</summary>
    static float Kick(double t, double beat) { double tb = (t % beat + beat) % beat; return (float)(0.8 * Math.Sin(2 * Math.PI * 55 * tb) * Math.Exp(-tb / 0.12)); }
    static float Hat(double t, double period, Random n) => (float)((n.NextDouble() * 2 - 1) * 0.25 * Math.Exp(-(t % period) / 0.02));
    static float Clap(double t, double beat) { double tb = t % beat; return (float)(0.3 * Math.Sin(2 * Math.PI * 1500 * tb) * Math.Exp(-tb / 0.015)); }
    /// <summary>Time as a drummer plays it: each beat up to 18 ms early or late (the same every run).</summary>
    static double Loose(double t, double beat)
    {
        static double Off(long i) => ((i * 7919 % 7 + 7) % 7 - 3) * 0.006;
        long i = (long)Math.Floor(t / beat);
        double on = i * beat + Off(i);
        if (t < on) { i--; on = i * beat + Off(i); }
        return t - on;
    }

    /// <summary>Plays sound(t) through an analyzer at a frame rate, as the apps do; <c>frame</c> runs after each frame.</summary>
    sealed class Synth(int fps)
    {
        public readonly AudioAnalyzer A = new() { SampleRate = 48000 };
        public double T;
        long n;
        readonly float[] buf = new float[48000];
        readonly Random noise = new(7);

        public void Play(double seconds, Func<double, Random, float> sound, Action? frame = null)
        {
            float dt = 1f / fps;
            for (double end = T + seconds; T < end;)
            {
                T += dt;
                long upto = (long)Math.Round(T * 48000);
                int c = (int)(upto - n);
                for (int i = 0; i < c; i++) buf[i] = sound((n + i) / 48000.0, noise);
                n = upto;
                A.Push(buf.AsSpan(0, c));
                A.Update(dt);
                frame?.Invoke();
            }
        }
    }

    static void Check(bool ok, string what) { if (!ok) throw new Exception("FALLO: " + what); }
    static void Eq(string a, string b) => Check(a == b, $"'{a}' != '{b}'");
    static void Near(float a, float b) => Check(MathF.Abs(a - b) < 1e-3f, $"{a} != {b}");
}
