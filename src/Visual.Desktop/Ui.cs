using System.Numerics;
using ImGuiNET;
using Visual.Core;

namespace Visual.Desktop;

sealed partial class App
{
    static readonly string[] KindNames = ["Constante", "Lineal", "Audio", "Lineal audio"];
    static readonly string[] ModeNames = ["Reiniciar", "Rebotar", "Rebotar invertido"];
    static readonly string[] BandNames = ["Graves", "Medios", "Agudos", "Melodía", "Nota"];
    static readonly string[] NoteNames = ["Do", "Do#", "Re", "Re#", "Mi", "Fa", "Fa#", "Sol", "Sol#", "La", "La#", "Si"];

    /// <summary>The note band (0..1) as a note name, e.g. "La 4".</summary>
    static string NoteName(float v)
    {
        double hz = AudioAnalyzer.NoteLo * Math.Pow(AudioAnalyzer.NoteHi / AudioAnalyzer.NoteLo, v);
        int midi = (int)Math.Round(69 + 12 * Math.Log2(hz / 440));
        return $"{NoteNames[(midi % 12 + 12) % 12]} {midi / 12 - 1}";
    }

    void BandMeter(int b)
    {
        float v = analyzer.Bands.Raw[b];
        ImGui.ProgressBar(Math.Clamp(v, 0, 1), new Vector2(-1, 0), b == (int)Band.Note ? $"Nota {(analyzer.Bands.Raw[(int)Band.Melody] > 0.03f ? NoteName(v) : "-")}" : $"{BandNames[b]} {v:0.00}");
    }
    static readonly Vector4 Red = new(1f, 0.45f, 0.45f, 1f);
    static readonly Vector4 Yellow = new(1f, 0.85f, 0.4f, 1f);
    static readonly Vector4 Green = new(0.4f, 0.9f, 0.5f, 1f);

    // block colors, the same as the web panel's cards (remote.html .c-*)
    static readonly Vector4 CScene = new(0.27f, 0.67f, 1f, 1f);   // #4af
    static readonly Vector4 CParam = new(0.2f, 0.8f, 0.8f, 1f);   // #3cc
    static readonly Vector4 CFx = new(0.67f, 0.47f, 1f, 1f);      // #a7f
    static readonly Vector4 CFiles = new(0.33f, 0.93f, 0.53f, 1f); // #5e8
    static readonly Vector4 CStrobe = new(1f, 0.4f, 0.4f, 1f);    // #f66
    static readonly Vector4 CDev = new(1f, 0.73f, 0.27f, 1f);     // #fb4

    List<AudioSource> sources = [];
    int selected = -1, addIndex;
    string status = "";

    void DrawUi()
    {
        ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(500, 760), ImGuiCond.FirstUseEver);
        ImGui.Begin("Visual   (F1 ocultar · F11 pantalla completa · N siguiente)");
        if (renderer.StrobeActive) Colored(Yellow, "STROBE"); else ImGui.TextUnformatted(""); // keeps the line so the form doesn't jump
        float foot = status != "" ? ImGui.GetTextLineHeightWithSpacing() * 2 + ImGui.GetStyle().ItemSpacing.Y : 0;
        if (ImGui.BeginTabBar("tabs"))
        {
            Tab("En vivo", LiveTab, foot);
            Tab("Escena", SceneTab, foot);
            Tab("Proyecto", ProjectTab, foot);
            Tab("Ajustes", SettingsTab, foot);
            ImGui.EndTabBar();
        }
        if (status != "") // fixed at the bottom, so it isn't lost at the end of a long tab
        {
            ImGui.Separator();
            if (ImGui.SmallButton("x##status")) status = "";
            ImGui.SameLine();
            ImGui.TextWrapped(status);
        }
        ImGui.End();
    }

    static void Tab(string name, Action body, float foot)
    {
        if (!ImGui.BeginTabItem(name)) return;
        ImGui.BeginChild("body", new Vector2(0, -foot));
        body();
        ImGui.EndChild();
        ImGui.EndTabItem();
    }

    // ---- colored blocks: a dark header in the block's color and a line along the open body ----
    readonly Stack<(float Y, Vector4 C)> sections = new();

    bool Section(string title, Vector4 c, bool open = true)
    {
        ImGui.PushStyleColor(ImGuiCol.Header, Shade(c, 0.28f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Shade(c, 0.4f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Shade(c, 0.5f));
        bool o = ImGui.CollapsingHeader(title, open ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
        ImGui.PopStyleColor(3);
        if (!o) return false;
        sections.Push((ImGui.GetCursorScreenPos().Y, c));
        ImGui.Indent(10);
        return true;
    }

    void EndSection()
    {
        var (y, c) = sections.Pop();
        ImGui.Unindent(10);
        var p = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(new Vector2(p.X + 3, y), new Vector2(p.X + 3, p.Y - 4), ImGui.GetColorU32(c), 2);
        ImGui.Spacing();
    }

    static Vector4 Shade(Vector4 c, float k) => new(c.X * k, c.Y * k, c.Z * k, 1);

    // ---- En vivo ----
    void LiveTab()
    {
        if (Section("Control###ctl", CStrobe))
        {
            if (ImGui.Button(project.Shuffle ? "Siguiente escena, al azar (N)" : "Siguiente escena (N)", new Vector2(-1, 0))) player.Next();
            var sc = project.Scenes;
            if (player.Current >= 0 && player.Current < sc.Count)
                ImGui.TextDisabled($"Sonando: {player.Current + 1}/{sc.Count} · {sc[player.Current].Title} · {player.SceneTime:0} s");
            EndSection();
        }
        if (Section("Audio###liveaudio", CDev))
        {
            for (int b = 0; b < AudioBands.Count; b++) BandMeter(b);
            if (capture.Error != null) Colored(Red, capture.Error);
            ImGui.TextDisabled("Fuente y ganancia: pestaña Ajustes.");
            EndSection();
        }
        if (Section("Música###music", CParam)) { MusicBody(); EndSection(); }
        if (Section("Auto DJ###autodj", CParam)) { AutoDjBody(); EndSection(); }
        if (Section("Escenas###scenes", CScene)) { ScenesList(); EndSection(); }
    }

    void MusicBody()
    {
        var m = analyzer.Music;
        ImGui.ProgressBar(m.Energy / 100, new Vector2(-1, 0), m.Silence ? "Silencio" : $"Energía {m.Energy:0} %");
        // BPM in bold (gray while unsure) and four lights for the beats of the bar, the current one fading out
        bool sure = m.Locked || m.Confidence >= 0.5f;
        if (!sure) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        BoldText(m.Bpm > 0 ? $"{m.Bpm:0.0} BPM{(m.Locked ? "  (fijo)" : "")}" : "-- BPM");
        if (!sure) ImGui.PopStyleColor();
        ImGui.SameLine(0, 20);
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        float r = 7, y = p.Y + ImGui.GetTextLineHeight() / 2;
        float glow = m.Bpm > 0 ? 1 - (float)(m.BarPhase % 1) : 0;
        for (int i = 0; i < 4; i++)
        {
            var c = i == m.Beat ? Shade(CParam, 0.35f + 0.65f * glow) : Shade(CParam, 0.2f);
            dl.AddCircleFilled(new Vector2(p.X + r + i * (2 * r + 8), y), r, ImGui.GetColorU32(c));
        }
        ImGui.Dummy(new Vector2(4 * (2 * r + 8), ImGui.GetTextLineHeight()));
        if (m.Bpm > 0 && !m.Locked)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(m.Confidence < MusicMeter.MinConfidence ? "sin golpes claros: es el último BPM" : $"confianza {m.Confidence * 100:0} %");
        }

        int taps = m.TapCount;
        ImGui.Button(taps is > 0 and < 4 ? $"Tap {taps}/4###tap" : "Tap###tap", new Vector2(90, 0));
        if (ImGui.IsItemActivated()) m.Tap(); // on press, not release: the moment of the beat
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Tocá 4 o más veces al ritmo, empezando en el 1 del compás. Fija ese BPM.");
        ImGui.SameLine();
        ImGui.BeginDisabled(m.Locked || m.Bpm == 0);
        if (ImGui.Button("Fijar")) m.Lock();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!m.Locked);
        if (ImGui.Button("Auto")) m.Unlock();
        ImGui.EndDisabled();
        ImGui.TextDisabled("Fijar mantiene el BPM actual; Auto vuelve a detectarlo con la música.");
    }

    static readonly int[] DjBars = [4, 8, 16, 32];
    static readonly string[] DjBarNames = ["4 compases", "8 compases", "16 compases", "32 compases"];

    void AutoDjBody()
    {
        var c = project.AutoDj;
        bool on = c.Enabled;
        if (ImGui.Checkbox("Cambiar escenas con la música", ref on)) c.Enabled = on;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Cambia en el 1 del compás, a una escena de intensidad parecida a la energía de la música");
        ImGui.BeginDisabled(!on);
        int b = Math.Max(0, Array.IndexOf(DjBars, c.Bars));
        ImGui.SetNextItemWidth(160);
        if (ImGui.Combo("Cambiar cada", ref b, DjBarNames, DjBarNames.Length)) c.Bars = DjBars[b];
        bool rnd = c.RandomBars;
        if (ImGui.Checkbox("Duración al azar (entre dos valores)", ref rnd)) c.RandomBars = rnd;
        if (rnd)
        {
            int mx = Math.Max(0, Array.IndexOf(DjBars, c.BarsMax));
            ImGui.SetNextItemWidth(160);
            if (ImGui.Combo("Máximo", ref mx, DjBarNames, DjBarNames.Length)) c.BarsMax = Math.Max(DjBars[mx], c.Bars);
        }
        if (c.BarsMax < c.Bars) c.BarsMax = c.Bars;
        bool drops = c.Drops;
        if (ImGui.Checkbox("Cambiar al cambiar la intensidad (y en drops)", ref drops)) c.Drops = drops;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("En un drop corta enseguida a una escena fuerte; si la energía sube o baja mucho, cambia en el próximo 1");
        float sens = c.Sensitivity;
        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderFloat("Sensibilidad", ref sens, 0, 1)) c.Sensitivity = sens;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Más alta = reacciona a cambios más chicos de energía");
        ImGui.EndDisabled();
        var dj = renderer.AutoDj;
        if (dj.Active) BoldText(dj.Status(c, project)); else ImGui.TextDisabled(dj.Status(c, project));
        if (dj.LastChange != "") ImGui.TextDisabled("Último cambio: " + dj.LastChange);
        ImGui.TextDisabled("La intensidad de cada escena se elige abajo (Auto = la mide mientras suena).");
    }

    /// <summary>Combo of a scene's intensity; Auto shows what was measured.</summary>
    static void IntensityCombo(string label, Scene s)
    {
        string auto = s.Motion < 0 ? "Auto (sin medir)" : $"Auto ({Scene.LevelNames[s.MeasuredLevel].ToLowerInvariant()})";
        string[] names = [auto, .. Scene.LevelNames[1..]];
        int v = s.Intensity;
        if (ImGui.Combo(label, ref v, names, names.Length)) s.Intensity = v;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Para el Auto DJ: suave en los momentos tranquilos, fuerte en los de más energía");
    }

    void ScenesList()
    {
        var scenes = project.Scenes;
        bool shuffle = project.Shuffle; // same setting as in the Proyecto tab
        if (ImGui.Checkbox("Orden aleatorio##live", ref shuffle)) project.Shuffle = shuffle;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Al terminar una escena (o con Siguiente) pasa a otra al azar");
        for (int i = 0; i < scenes.Count; i++)
        {
            var s = scenes[i];
            bool playing = i == player.Current;
            // ▶ is the only way to switch here (a click on the name just selects it for the buttons below)
            if (playing) ImGui.PushStyleColor(ImGuiCol.Button, Shade(CScene, 0.6f));
            if (ImGui.ArrowButton($"##play{i}", ImGuiDir.Right)) player.GoTo(i);
            if (playing) ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Pasar a esta escena ahora");
            ImGui.SameLine();
            var dur = s.Duration > 0 ? $"{s.Duration:0.#} s" : "manual";
            var fx = string.Join(" · ", EffectDefs.ActiveLabels(s.Effects));
            var lvl = Scene.LevelNames[s.Level].ToLowerInvariant() + (s.Layers.Count switch { 0 => "", 1 => " · 1 capa", var c => $" · {c} capas" });
            if (ImGui.Selectable($"{i + 1}. {s.Title}   [{dur} · {lvl}]{(fx == "" ? "" : "   " + fx)}##scene{i}", i == selected)) selected = i;
            if (playing) SceneProgress(s);
        }
        if (scenes.Count == 0) ImGui.TextDisabled("Sin escenas: agregá shaders abajo.");
        if (selected >= scenes.Count) selected = scenes.Count - 1;

        if (selected >= 0)
        {
            if (ImGui.Button("Subir") && selected > 0) { player.Move(selected, selected - 1); selected--; }
            ImGui.SameLine();
            if (ImGui.Button("Bajar") && selected < scenes.Count - 1) { player.Move(selected, selected + 1); selected++; }
            ImGui.SameLine();
            if (ImGui.Button("Quitar")) player.Remove(selected);
            if (selected >= 0)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(110);
                float d = scenes[selected].Duration;
                if (ImGui.InputFloat("Duración (s, 0 = manual)##live", ref d, 1, 10, "%.1f")) scenes[selected].Duration = Math.Max(0, d);
                ImGui.SetNextItemWidth(160);
                IntensityCombo("Intensidad##live", scenes[selected]);
            }
        }

        ImGui.Separator();
        if (shaderFiles.Length == 0) ImGui.TextDisabled("No hay archivos .fs en la carpeta (pestaña Ajustes).");
        else
        {
            addIndex = Math.Clamp(addIndex, 0, shaderFiles.Length - 1);
            ImGui.Combo("##addshader", ref addIndex, shaderFiles, shaderFiles.Length);
            ImGui.SameLine();
            if (ImGui.Button("Agregar escena"))
            {
                scenes.Add(new Scene { Shader = shaderFiles[addIndex] });
                selected = scenes.Count - 1;
            }
        }
    }

    /// <summary>Under the playing scene: "12 / 30 s" and a bar up to the change, or just the seconds when it is manual.</summary>
    void SceneProgress(Scene s)
    {
        float x = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X; // lined up with the name
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + x);
        double t = player.SceneTime;
        if (player.AutoDriving) // the Auto DJ decides: bars instead of seconds
            ImGui.ProgressBar(Math.Clamp(renderer.AutoDj.Bar / (float)renderer.AutoDj.Target(project.AutoDj), 0, 1), new Vector2(-1, 0),
                $"Auto DJ · compás {renderer.AutoDj.Bar + 1} / {renderer.AutoDj.Target(project.AutoDj)}");
        else if (s.Duration > 0)
            ImGui.ProgressBar((float)Math.Clamp(t / s.Duration, 0, 1), new Vector2(-1, 0), $"{t:0} / {s.Duration:0.#} s");
        else ImGui.TextDisabled($"{t:0} s");
    }

    // ---- Escena ----
    void SceneTab()
    {
        if (selected < 0 || selected >= project.Scenes.Count)
        {
            ImGui.TextDisabled("Elegí una escena en la pestaña En vivo.");
            return;
        }
        var scene = project.Scenes[selected];
        ImGui.Text($"Escena {selected + 1}: {scene.Title}");
        if (Section("General###sgen", CScene))
        {
            string sn = scene.Name;
            if (ImGui.InputTextWithHint("Nombre", scene.Shader, ref sn, 60)) scene.Name = sn;
            float d = scene.Duration;
            if (ImGui.InputFloat("Duración (s, 0 = manual)", ref d, 1, 10, "%.1f")) scene.Duration = Math.Max(0, d);
            IntensityCombo("Intensidad", scene);
            if (ImGui.Button("Reproducir esta escena")) player.GoTo(selected);
            EndSection();
        }
        if (Section("Lienzo###scanvas", CScene, false)) { SceneCanvasEditor(scene); EndSection(); }
        var prog = renderer.Get(scene.Shader);
        if (Section("Parámetros###sparams", CParam))
        {
            if (prog.Error != null) Colored(Red, prog.Error);
            if (prog.Isf.Description != "") ImGui.TextWrapped(prog.Isf.Description);
            prog.Isf.EnsureParams(scene.Params);
            double time = selected == player.Current ? player.SceneTime : 0;
            foreach (var i in prog.Isf.Inputs) InputEditor(i, scene, time);
            if (prog.Isf.Inputs.Count == 0) ImGui.TextDisabled("Este shader no tiene parámetros.");
            EndSection();
        }
        EffectsSections(scene);
        LayersSection(scene, time: selected == player.Current ? player.SceneTime : 0);
    }

    int layerAddIndex;

    /// <summary>The scene's layers, in drawing order: each one its shader, blend, parameters and effects.</summary>
    void LayersSection(Scene scene, double time)
    {
        if (!Section($"Capas ({scene.Layers.Count})###slayers", CScene)) return;
        ImGui.TextDisabled("Se dibujan sobre el shader de la escena, en orden; los efectos de la escena van encima de todo.");
        Action? later = null; // list changes after the loop
        for (int i = 0; i < scene.Layers.Count; i++)
        {
            var l = scene.Layers[i];
            int k = i;
            ImGui.PushID(i);
            bool vis = !l.Off;
            if (ImGui.Checkbox("##vis", ref vis)) l.Off = !vis;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(vis ? "Ocultar sin perder la configuración" : "Mostrar");
            ImGui.SameLine();
            if (ImGui.ArrowButton("##up", ImGuiDir.Up) && i > 0) later = () => { scene.Layers.RemoveAt(k); scene.Layers.Insert(k - 1, l); };
            ImGui.SameLine();
            if (ImGui.ArrowButton("##down", ImGuiDir.Down) && i < scene.Layers.Count - 1) later = () => { scene.Layers.RemoveAt(k); scene.Layers.Insert(k + 1, l); };
            ImGui.SameLine();
            var prog = renderer.Get(l.Shader);
            var fx = string.Join(" · ", EffectDefs.ActiveLabels(l.Effects));
            if (l.Off) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.45f);
            bool open = ImGui.TreeNode($"{i + 1}. {l.Shader}   [{Layer.BlendNames[(int)l.Blend]}]{(fx == "" ? "" : "   " + fx)}###layer");
            if (l.Off) ImGui.PopStyleVar();
            if (open)
            {
                int si = Math.Max(0, Array.IndexOf(shaderFiles, l.Shader));
                if (shaderFiles.Length > 0 && ImGui.Combo("Shader", ref si, shaderFiles, shaderFiles.Length)) l.Shader = shaderFiles[si];
                int b = Enum.IsDefined(l.Blend) ? (int)l.Blend : 0;
                if (ImGui.Combo("Blend", ref b, Layer.BlendNames, Layer.BlendNames.Length)) l.Blend = (BlendMode)b;
                if (ImGui.Button("Duplicar")) later = () => scene.Layers.Insert(k + 1, l.Clone());
                ImGui.SameLine();
                if (ImGui.Button("Quitar capa")) later = () => scene.Layers.RemoveAt(k);
                if (prog.Error != null) Colored(Red, prog.Error);
                prog.Isf.EnsureParams(l.Params);
                bool filter = prog.Isf.Inputs.Any(x => x.Name == Layer.Below && x.IsTexture);
                if (filter) ImGui.TextDisabled("Filtro: recibe la imagen de lo que está debajo.");
                if (ImGui.TreeNodeEx("Parámetros", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    foreach (var inp in prog.Isf.Inputs) InputEditor(inp, l, time, filter);
                    if (prog.Isf.Inputs.Count == 0) ImGui.TextDisabled("Este shader no tiene parámetros.");
                    ImGui.TreePop();
                }
                if (ImGui.TreeNodeEx("Efectos de la capa", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    EffectsBody(l);
                    ImGui.TreePop();
                }
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
        later?.Invoke();
        if (shaderFiles.Length > 0 && scene.Layers.Count < Remote.MaxLayers)
        {
            layerAddIndex = Math.Clamp(layerAddIndex, 0, shaderFiles.Length - 1);
            ImGui.Combo("##addlayer", ref layerAddIndex, shaderFiles, shaderFiles.Length);
            ImGui.SameLine();
            if (ImGui.Button("Agregar capa"))
            {
                var nl = new Layer { Shader = shaderFiles[layerAddIndex] };
                nl.EnsureEffects();
                scene.Layers.Add(nl);
            }
        }
        EndSection();
    }

    static readonly float[] Scales = [1, 0.75f, 0.5f, 0.35f]; // same as the TV
    static readonly string[] ScaleNames = ["100 %", "75 %", "50 %", "35 %"];

    static readonly string[] CanvasLabels = ["Pantalla completa", "16:9 centrado", "4:3 centrado", "Cuadrado 1:1", "Personalizado (píxeles)"];

    static void SceneCanvasEditor(Scene scene)
    {
        int qi = scene.Quality > 0 ? 1 + Array.IndexOf(Scales, Scales.MinBy(s => MathF.Abs(s - scene.Quality))) : 0;
        if (ImGui.Combo("Calidad de esta escena", ref qi, ["Global", .. ScaleNames], ScaleNames.Length + 1)) scene.Quality = qi == 0 ? 0 : Scales[qi - 1];
        bool own = scene.Canvas != null;
        if (ImGui.Checkbox("Lienzo propio (reemplaza el global)", ref own)) scene.Canvas = own ? new SceneCanvas() : null;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Solo para esta escena. En píxeles de la salida (visor, ventana de salida o pantalla del TV).");
        if (scene.Canvas is not { } c) return;
        int m = Math.Max(0, Array.IndexOf(SceneCanvas.Modes, c.Mode));
        if (ImGui.Combo("Lienzo", ref m, CanvasLabels, CanvasLabels.Length)) c.Mode = SceneCanvas.Modes[m];
        if (c.Mode != "px") return;
        int x = c.X, y = c.Y, w = c.W, h = c.H;
        if (ImGui.InputInt("X (px)##cv", ref x)) c.X = Math.Max(0, x);
        if (ImGui.InputInt("Y (px)##cv", ref y)) c.Y = Math.Max(0, y);
        if (ImGui.InputInt("Ancho (px)##cv", ref w)) c.W = Math.Max(1, w);
        if (ImGui.InputInt("Alto (px)##cv", ref h)) c.H = Math.Max(1, h);
    }

    void InputEditor(IsfInput i, Picture scene, double time, bool filter = false)
    {
        var ps = scene.Params;
        ImGui.PushID(i.Name);
        float[] v = i.IsTexture ? [] : ps[i.Name].Value;
        switch (i.Type)
        {
            case IsfType.Float:
                ParamEditor(i.Label, ps[i.Name], i.Min[0], i.Max[0], null, time);
                break;
            case IsfType.Long:
                int idx = Array.IndexOf(i.Values, (int)MathF.Round(v[0]));
                if (ImGui.Combo(i.Label, ref idx, i.Labels, i.Labels.Length)) v[0] = i.Values[idx];
                break;
            case IsfType.Bool:
                bool b = v[0] > 0.5f;
                if (ImGui.Checkbox(i.Label, ref b)) v[0] = b ? 1 : 0;
                break;
            case IsfType.Event:
                if (ImGui.Button(i.Label)) v[0] = 1;
                break;
            case IsfType.Point2D:
                ImGui.SliderFloat(i.Label + " X", ref v[0], i.Min[0], i.Max[0]);
                ImGui.SliderFloat(i.Label + " Y", ref v[1], i.Min[1], i.Max[1]);
                break;
            case IsfType.Color:
                var c = new Vector4(v[0], v[1], v[2], v[3]);
                if (ImGui.ColorEdit4(i.Label, ref c)) (v[0], v[1], v[2], v[3]) = (c.X, c.Y, c.Z, c.W);
                break;
            case IsfType.Audio or IsfType.AudioFFT:
                ImGui.TextDisabled($"{i.Label}: audio automático");
                break;
            default:
                MediaEditor(i, scene, filter && i.Name == Layer.Below);
                break;
        }
        ImGui.PopID();
    }

    /// <summary>Video / picture of an image input: a file of the media folder or a URL, plus speed.</summary>
    void MediaEditor(IsfInput i, Picture scene, bool below = false)
    {
        var m = scene.Media.GetValueOrDefault(i.Name);
        var src = m?.Src ?? "";
        var items = new[] { below ? "(lo que está debajo)" : "(nada: negro)" }.Concat(mediaFiles).Append("URL (http / https)...").ToArray();
        int cur = src == "" ? 0 : Media.IsUrl(src) ? items.Length - 1 : Math.Max(0, Array.IndexOf(mediaFiles, src) + 1);
        if (src != "" && !Media.IsUrl(src) && cur == 0) ImGui.TextColored(Red, $"No está en la carpeta de videos: {src}");
        if (ImGui.Combo(i.Label, ref cur, items, items.Length))
        {
            var s = cur == 0 ? "" : cur == items.Length - 1 ? "https://" : mediaFiles[cur - 1];
            if (s == "") scene.Media.Remove(i.Name);
            else scene.Media[i.Name] = m = new SceneMedia { Src = s, Speed = m?.Speed ?? 1 };
        }
        if (scene.Media.GetValueOrDefault(i.Name) is not { } sm) return;
        if (Media.IsUrl(sm.Src))
        {
            string url = sm.Src;
            if (ImGui.InputText("URL##media", ref url, 2000, ImGuiInputTextFlags.EnterReturnsTrue)) sm.Src = url.Trim();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Dirección directa a un .mp4 / .webm (Enter para aplicar).\nYouTube y otras páginas no funcionan.");
        }
        float sp = sm.Speed;
        if (ImGui.SliderFloat("Velocidad##media", ref sp, SceneMedia.MinSpeed, SceneMedia.MaxSpeed, "x%.2f", ImGuiSliderFlags.Logarithmic)) sm.Speed = sp;
        if (renderer.MediaError(scene, i.Name) is { } err) Colored(Red, err);
    }

    /// <summary>"Curva" combo: each option with a little drawing of its curve.</summary>
    static void CurveCombo(Modifier m)
    {
        const string pad = "       ";
        float s = ImGui.GetTextLineHeight();
        uint col = ImGui.GetColorU32(ImGuiCol.Text);
        if (!Enum.IsDefined(m.Ease)) m.Ease = Ease.Linear;
        bool open = ImGui.BeginCombo("Curva", pad + Curves.Names[(int)m.Ease], ImGuiComboFlags.HeightLarge);
        var fr = ImGui.GetItemRectMin();
        if (open)
        {
            for (int i = 0; i < Curves.Names.Length; i++)
            {
                var p = ImGui.GetCursorScreenPos();
                if (ImGui.Selectable(pad + Curves.Names[i] + "##c" + i, (int)m.Ease == i)) m.Ease = (Ease)i;
                CurveIcon(ImGui.GetWindowDrawList(), p, s, (Ease)i, m.Steps, col);
            }
            ImGui.EndCombo();
        }
        CurveIcon(ImGui.GetWindowDrawList(), fr + ImGui.GetStyle().FramePadding, s, m.Ease, m.Steps, col);
    }

    static void CurveIcon(ImDrawListPtr dl, Vector2 p, float s, Ease e, int steps, uint col)
    {
        const int n = 32;
        float w = s * 1.6f;
        dl.AddRect(p, p + new Vector2(w, s), col & 0x40FFFFFF);
        Span<Vector2> pts = stackalloc Vector2[n + 1];
        for (int i = 0; i <= n; i++)
            pts[i] = p + new Vector2(w * i / n, s - 1 - (s - 2) * (float)Curves.Of(e, Math.Clamp(steps, 2, 16), i / (double)n));
        for (int i = 0; i < n; i++) dl.AddLine(pts[i], pts[i + 1], col, 1.5f);
    }

    /// <summary>One line: slider (limited to the range) · modifier · arrow for the details (range, invert, modifier settings).
    /// <paramref name="boldName"/>: the name in bold (effects). Returns whether the details are open.</summary>
    bool ParamEditor(string label, ParamState ps, float min, float max, string[]? labels, double time, bool boldName = false)
    {
        ImGui.PushID(label);
        if (labels != null)
        {
            int idx = (int)MathF.Round(ps.Value[0]);
            if (ImGui.Combo(boldName ? "##v" : label, ref idx, labels, labels.Length)) ps.Value[0] = idx;
            if (boldName)
            {
                ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
                BoldText(label);
            }
            ImGui.PopID();
            return false;
        }
        var m = ps.Mod;
        var (lo, hi) = m.Range(min, max);
        float sp = ImGui.GetStyle().ItemInnerSpacing.X, kindW = 92, arrowW = ImGui.GetFrameHeight();
        float sliderW = Math.Max(40, ImGui.CalcItemWidth() - kindW - arrowW - 2 * sp);
        float v = Math.Clamp(ps.Value[0], lo, hi);
        ImGui.SetNextItemWidth(sliderW);
        if (ImGui.SliderFloat("##v", ref v, lo, hi)) ps.Value[0] = v;
        ImGui.SameLine(0, sp);
        ImGui.SetNextItemWidth(kindW);
        int kind = (int)m.Kind;
        if (ImGui.Combo("##kind", ref kind, KindNames, KindNames.Length)) m.Kind = (ModKind)kind;
        ImGui.SameLine(0, sp);
        var st = ImGui.GetStateStorage();
        uint id = ImGui.GetID("more");
        bool open = st.GetBool(id);
        if (ImGui.ArrowButton("##more", open ? ImGuiDir.Down : ImGuiDir.Right)) st.SetBool(id, open = !open);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Rango, invertir y ajustes del modificador");
        ImGui.SameLine(0, sp);
        bool narrowed = m.From > 0 || m.To < 1 || m.Invert;
        if (boldName) BoldText(narrowed ? label + " *" : label);
        else ImGui.TextUnformatted(narrowed ? label + " *" : label);
        if (narrowed && ImGui.IsItemHovered()) ImGui.SetTooltip(m.Invert ? $"Rango {lo:0.###} a {hi:0.###}, invertido" : $"Rango {lo:0.###} a {hi:0.###}");
        if (m.Kind != ModKind.Constant) // thin live bar under the slider: where the modifier has it now
        {
            float o = m.Apply(ps.Value[0], min, max, time, analyzer.Bands);
            ImGui.ProgressBar(hi != lo ? (o - lo) / (hi - lo) : 0, new Vector2(sliderW, 3), "");
        }
        if (open)
        {
            ImGui.Indent();
            float f = lo, t = hi;
            if (ImGui.SliderFloat("Desde", ref f, min, max)) m.From = Math.Min(Frac(f, min, max), m.To);
            if (ImGui.SliderFloat("Hasta", ref t, min, max)) m.To = Math.Max(Frac(t, min, max), m.From);
            if (m.From > 0 || m.To < 1)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Rango completo")) (m.From, m.To) = (0, 1);
            }
            if (m.Kind != ModKind.Constant)
            {
                bool inv = m.Invert;
                if (ImGui.Checkbox(m.Kind != ModKind.Audio ? "Invertir (recorre al revés)" : "Invertir (más sonido = menos valor)", ref inv)) m.Invert = inv;
            }
            if (m.Kind is ModKind.Linear or ModKind.LinearAudio)
            {
                float speed = m.Speed;
                if (ImGui.SliderFloat(m.Kind == ModKind.Linear ? "Velocidad (recorridos/s)" : "Velocidad máxima (recorridos/s)", ref speed, 0.001f, 5f, "%.3f", ImGuiSliderFlags.Logarithmic)) m.Speed = speed;
                int mode = (int)m.Mode;
                if (ImGui.Combo("Al llegar al borde", ref mode, ModeNames, ModeNames.Length)) m.Mode = (LinearMode)mode;
                CurveCombo(m);
                if (m.Ease is Ease.Steps or Ease.RandomSteps or Ease.RandomSmooth)
                {
                    int steps = Math.Clamp(m.Steps, 2, 16);
                    if (ImGui.SliderInt(m.Ease == Ease.Steps ? "Pasos" : "Valores por recorrido", ref steps, 2, 16)) m.Steps = steps;
                }
            }
            if (m.Kind is ModKind.Audio or ModKind.LinearAudio)
            {
                int band = (int)m.Band;
                if (ImGui.Combo("Banda", ref band, BandNames, BandNames.Length)) m.Band = (Band)band;
                if (m.Kind == ModKind.LinearAudio) ImGui.TextDisabled("Avanza más rápido cuanto más pasa la banda del umbral; debajo se queda quieto.");
                float a = m.Amount;
                if (m.Kind == ModKind.Audio && ImGui.SliderFloat("Intensidad", ref a, -1f, 1f)) m.Amount = a;
                float g = m.Gain;
                if (ImGui.SliderFloat("Ganancia", ref g, 0.1f, 10f, "x%.2f", ImGuiSliderFlags.Logarithmic)) m.Gain = g;
                float th = m.Threshold;
                if (ImGui.SliderFloat("Umbral", ref th, 0f, 0.95f)) m.Threshold = th;
                bool ag = m.AutoGain;
                if (ImGui.Checkbox("Auto-ganancia", ref ag)) m.AutoGain = ag;
            }
            ImGui.Unindent();
            ImGui.Spacing();
        }
        ImGui.PopID();
        return open;
    }

    void BoldText(string s)
    {
        if (bold is { } f) ImGui.PushFont(f);
        ImGui.TextUnformatted(s);
        if (bold != null) ImGui.PopFont();
    }

    static float Frac(float v, float min, float max) => max != min ? Math.Clamp((v - min) / (max - min), 0, 1) : 0;

    // effects in use (with their siblings: strobe, mask, monocolor) on top, the rest folded below;
    // the split waits while something is being dragged, so an effect doesn't jump away under the mouse
    readonly Dictionary<Picture, HashSet<string>> fxInUse = [];

    (EffectDef[] Used, EffectDef[] Others) SplitEffects(Picture pic)
    {
        pic.EnsureEffects();
        if (fxInUse.Count > 64) fxInUse.Clear(); // pictures of closed projects
        if (!ImGui.IsAnyItemActive() || !fxInUse.ContainsKey(pic))
            fxInUse[pic] = EffectDefs.All.Where(d => EffectDefs.IsActive(d, pic.Effects[d.Name])).Select(d => EffectDefs.Group(d.Name)).ToHashSet();
        var used = EffectDefs.All.Where(d => fxInUse[pic].Contains(EffectDefs.Group(d.Name))).ToArray();
        return (used, EffectDefs.All.Except(used).ToArray());
    }

    /// <summary>A layer's effects: in use, reset, and the rest folded.</summary>
    void EffectsBody(Picture pic)
    {
        var (used, rest) = SplitEffects(pic);
        if (used.Length == 0) ImGui.TextDisabled("Ninguno en uso.");
        foreach (var d in used) EffectRow(d, pic.Effects[d.Name]);
        if (ImGui.Button("Restablecer efectos")) { pic.Effects.Clear(); pic.EnsureEffects(); }
        if (ImGui.TreeNode($"Más efectos ({rest.Length})###more"))
        {
            foreach (var d in rest) EffectRow(d, pic.Effects[d.Name]);
            ImGui.TreePop();
        }
    }

    void EffectsSections(Scene scene)
    {
        var (used, rest) = SplitEffects(scene);
        if (Section($"Efectos activos ({used.Length})###fxon", CFx))
        {
            if (used.Length == 0) ImGui.TextDisabled("Ninguno. Abrí \"Más efectos\" y mové alguno.");
            foreach (var d in used) EffectRow(d, scene.Effects[d.Name]);
            if (ImGui.Button("Restablecer efectos"))
            {
                scene.Effects.Clear();
                scene.EnsureEffects();
            }
            ImGui.SameLine();
            if (ImGui.Button("Copiar efectos a todas las escenas")) ImGui.OpenPopup("Copiar efectos");
            if (ImGui.BeginPopupModal("Copiar efectos"))
            {
                ImGui.Text("Los efectos de las otras escenas se reemplazan por estos.");
                if (ImGui.Button("Copiar")) { project.CopyEffects(scene); ImGui.CloseCurrentPopup(); }
                ImGui.SameLine();
                if (ImGui.Button("Cancelar")) ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
            EndSection();
        }
        if (Section($"Más efectos ({rest.Length})###fxoff", CFx, false))
        {
            foreach (var d in rest) EffectRow(d, scene.Effects[d.Name]);
            EndSection();
        }
    }

    // with its details open, the whole effect sits in a darker framed box: drawn on a back channel once its height is known
    static readonly Vector4 FxBoxBg = new(0.025f, 0.025f, 0.03f, 1);

    void EffectRow(EffectDef d, ParamState ps)
    {
        var dl = ImGui.GetWindowDrawList();
        var top = ImGui.GetCursorScreenPos();
        float right = top.X + ImGui.GetContentRegionAvail().X;
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        bool open = EffectRowBody(d, ps);
        dl.ChannelsSetCurrent(0);
        if (open)
        {
            var bottom = ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y + 4;
            Vector2 a = new(top.X - 6, top.Y - 4), b = new(right + 2, bottom);
            dl.AddRectFilled(a, b, ImGui.GetColorU32(FxBoxBg), 6);
            dl.AddRect(a, b, ImGui.GetColorU32(Shade(CFx, 0.5f)), 6);
        }
        dl.ChannelsMerge();
        if (open) ImGui.Dummy(new Vector2(0, 6)); // room below the frame
    }

    // effects ticked while still at their default (not in use yet)
    readonly HashSet<ParamState> armed = [];

    bool EffectRowBody(EffectDef d, ParamState ps)
    {
        // ticked = in use (or just ticked to start moving it); unticked = locked; unticking keeps the settings (Off)
        bool active = EffectDefs.IsActive(d, ps), en = active || armed.Contains(ps);
        if (ImGui.Checkbox($"##on{d.Name}", ref en))
        {
            if (en) { armed.Add(ps); ps.Off = false; }
            else { armed.Remove(ps); if (active) ps.Off = true; }
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(en ? "Desactivar sin perder la configuración" : "Activar para poder moverlo");
        ImGui.SameLine();
        ActiveDot(EffectDefs.IsActive(d, ps));
        ImGui.BeginDisabled(!en);
        bool open = ParamEditor(d.Label, ps, d.Min, d.Max, d.Labels, renderer.Time, boldName: true);
        ImGui.EndDisabled();
        if (d.Name == "strobeRate" && !settings.AllowFastStrobe)
            ImGui.TextDisabled("Limitado a 3 Hz (se cambia en Ajustes)");
        return open;
    }

    /// <summary>Green dot in front of the next widget when the effect is in use; keeps the alignment otherwise.</summary>
    static void ActiveDot(bool on)
    {
        float h = ImGui.GetFrameHeight();
        var p = ImGui.GetCursorScreenPos();
        if (on) ImGui.GetWindowDrawList().AddCircleFilled(p + new Vector2(5, h / 2), 4, ImGui.GetColorU32(Green));
        ImGui.Dummy(new Vector2(12, h));
        ImGui.SameLine();
    }

    // ---- Proyecto ----
    void ProjectTab()
    {
        if (Section("Proyecto###proj", CScene)) { ProjectBody(); EndSection(); }
        if (Section("Auto strobe###auto", CStrobe)) { AutoStrobeBody(); EndSection(); }
    }

    void ProjectBody()
    {
        int cur = Array.IndexOf(projectNames, loadedName);
        if (ImGui.Combo("Abrir", ref cur, projectNames, projectNames.Length) && projectNames[cur] != loadedName)
        {
            SaveProject();
            OpenProject(projectNames[cur]);
        }
        string name = project.Name;
        if (ImGui.InputText("Nombre", ref name, 64)) project.Name = name;

        if (ImGui.Button("Guardar")) SaveProject();
        ImGui.SameLine();
        if (ImGui.Button("Nuevo")) NewProject();
        ImGui.SameLine();
        if (ImGui.Button("Duplicar")) DuplicateProject();
        ImGui.SameLine();
        if (ImGui.Button("Borrar")) ImGui.OpenPopup("Borrar proyecto");
        if (ImGui.BeginPopupModal("Borrar proyecto"))
        {
            ImGui.Text($"¿Borrar \"{project.Name}\"? No se puede deshacer.");
            if (ImGui.Button("Sí, borrar"))
            {
                if (loadedName != "") store.Delete(loadedName);
                loadedName = "";
                OpenProject(null);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancelar")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        if (ImGui.Button("Exportar JSON..."))
        {
            using var dlg = new System.Windows.Forms.SaveFileDialog { Filter = "Proyecto (*.json)|*.json", FileName = ProjectFileName(project.Name) };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                try { File.WriteAllText(dlg.FileName, ProjectStore.ToJson(project)); status = "Exportado: " + dlg.FileName; }
                catch (Exception e) { status = "No se pudo exportar: " + e.Message; }
        }
        ImGui.SameLine();
        if (ImGui.Button("Importar JSON..."))
        {
            using var dlg = new System.Windows.Forms.OpenFileDialog { Filter = "Proyecto (*.json)|*.json" };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                try { ImportProject(File.ReadAllText(dlg.FileName)); }
                catch (Exception e) { status = "No se pudo importar: " + e.Message; }
        }

        bool shuffle = project.Shuffle;
        if (ImGui.Checkbox("Orden aleatorio", ref shuffle)) project.Shuffle = shuffle;
        bool fade = project.Crossfade;
        if (ImGui.Checkbox("Fundido entre escenas", ref fade)) project.Crossfade = fade;
        if (fade)
        {
            float s = project.CrossfadeSeconds;
            if (ImGui.SliderFloat("Duración del fundido (s)", ref s, 0.1f, 10f)) project.CrossfadeSeconds = s;
        }
    }

    void StartNew(Project p)
    {
        project = p;
        project.EnsureEffects();
        loadedName = "";
        player.Start(project);
        selected = project.Scenes.Count > 0 ? 0 : -1;
        SaveProject();
    }

    static string ProjectFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".json";

    static readonly string[] RhythmNames = ["Lineal (Strobe Hz)", "Sigue la banda"];
    static readonly string[] DurationNames = ["Fija", "Al azar"];
    static readonly string[] EndNames = ["Seguir en la escena", "Pasar a la siguiente"];

    static readonly string[] PickNames = ["En orden", "Al azar"];

    void AutoStrobeBody()
    {
        var a = project.AutoStrobe;
        a.Ensure();
        bool on = a.Enabled;
        if (ImGui.Checkbox("Activado##auto", ref on)) a.Enabled = on;
        var run = renderer.AutoStrobe;
        if (on)
            Colored(run.Active ? Yellow : run.Blocked ? Red : Green,
                run.Active ? $"Disparando: {ConfigTitle(a, run.Current)}"
                : run.Blocked ? "Bloqueado: la escena que suena tiene su propio strobe (Strobe Hz > 0)"
                : run.Upcoming(a) < 0 ? "Ninguna configuración activa"
                : run.CooldownLeft > 0 ? $"Enfriamiento: {run.CooldownLeft:0.0} s"
                : "Esperando que la banda supere el umbral");
        ImGui.PushID("auto");
        if (on)
        {
            int band = (int)a.Band;
            if (ImGui.Combo("Banda", ref band, BandNames, BandNames.Length)) a.Band = (Band)band;
            float g = a.Gain;
            if (ImGui.SliderFloat("Ganancia", ref g, 0.1f, 10f, "x%.2f", ImGuiSliderFlags.Logarithmic)) a.Gain = g;
            bool ag = a.AutoGain;
            if (ImGui.Checkbox("Autoganancia", ref ag)) a.AutoGain = ag;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Mide la banda contra su pico reciente: reacciona igual con temas suaves y fuertes.");
            float t = a.Threshold;
            if (ImGui.SliderFloat("Umbral", ref t, 0.05f, 1f)) a.Threshold = t;
            float level = analyzer.Bands.Get(a.Band, a.AutoGain) * a.Gain;
            ImGui.ProgressBar(Math.Clamp(level, 0, 1), new Vector2(-1, 0), level >= a.Threshold ? "supera el umbral" : $"{level:0.00}");
            float p = a.Probability;
            if (ImGui.SliderFloat("Probabilidad", ref p, 0f, 100f, "%.0f %%")) a.Probability = p;
            float c = a.Cooldown;
            if (ImGui.SliderFloat("Enfriamiento (s)", ref c, 0f, 120f)) a.Cooldown = c;
        }
        int pick = a.Shuffle ? 1 : 0;
        if (ImGui.Combo("Elegir configuración", ref pick, PickNames, PickNames.Length)) a.Shuffle = pick == 1;

        ImGui.SeparatorText("Configuraciones");
        Action? later = null; // list changes after the loop
        int playing = run.Active ? run.Current : -1, upcoming = run.Upcoming(a);
        for (int i = 0; i < a.Configs.Count; i++)
        {
            var f = a.Configs[i];
            int k = i;
            ImGui.PushID(i);
            bool act = f.On;
            if (ImGui.Checkbox("##on", ref act)) f.On = act;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(act ? "Sacar de la rotación sin perder la configuración" : "Volver a la rotación");
            ImGui.SameLine();
            if (ImGui.ArrowButton("##up", ImGuiDir.Up) && i > 0) later = () => { a.Configs.RemoveAt(k); a.Configs.Insert(k - 1, f); };
            ImGui.SameLine();
            if (ImGui.ArrowButton("##down", ImGuiDir.Down) && i < a.Configs.Count - 1) later = () => { a.Configs.RemoveAt(k); a.Configs.Insert(k + 1, f); };
            ImGui.SameLine();
            string tag = i == playing ? "   << disparando" : i == upcoming ? "   (la próxima)" : "";
            if (!f.On) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.45f);
            if (i == playing) ImGui.PushStyleColor(ImGuiCol.Text, Yellow);
            bool open = ImGui.TreeNode($"{ConfigTitle(a, i)}{tag}###cfg");
            if (i == playing) ImGui.PopStyleColor();
            if (!f.On) ImGui.PopStyleVar();
            if (open)
            {
                string name = f.Name;
                if (ImGui.InputText("Nombre", ref name, 60)) f.Name = name;
                ConfigBody(f);
                if (a.Configs.Count < AutoStrobe.MaxConfigs && ImGui.Button("Duplicar")) later = () => a.Configs.Insert(k + 1, f.Clone());
                if (a.Configs.Count > 1)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Quitar")) later = () => a.Configs.RemoveAt(k);
                }
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
        later?.Invoke();
        if (a.Configs.Count < AutoStrobe.MaxConfigs && ImGui.Button("Agregar configuración")) a.Configs.Add(new());
        ImGui.TextDisabled("Cada ráfaga usa una configuración. El botón STROBE del teléfono usa la que toca a continuación.");
        ImGui.TextDisabled("No se dispara mientras la escena que suena tiene su propio strobe.");
        ImGui.PopID();
    }

    static string ConfigTitle(AutoStrobe a, int i) =>
        a.Configs.ElementAtOrDefault(i) is { } f ? (f.Name.Trim() != "" ? $"{i + 1}. {f.Name.Trim()}" : $"{i + 1}. {EffectDefs.StrobeModes[Math.Clamp(f.Mode, 0, EffectDefs.StrobeModes.Length - 1)]} {f.Rate:0.#} Hz") : "";

    /// <summary>One burst configuration: duration, rhythm, what happens after, and its flash.</summary>
    void ConfigBody(StrobeConfig f)
    {
        int dm = f.RandomDuration ? 1 : 0;
        if (ImGui.Combo("Duración", ref dm, DurationNames, DurationNames.Length)) f.RandomDuration = dm == 1;
        if (f.RandomDuration)
        {
            float mn = f.MinDuration, mx = f.MaxDuration;
            if (ImGui.SliderFloat("Mínimo (s)", ref mn, 0.1f, 30f)) f.MinDuration = Math.Min(mn, f.MaxDuration);
            if (ImGui.SliderFloat("Máximo (s)", ref mx, 0.1f, 30f)) f.MaxDuration = Math.Max(mx, f.MinDuration);
        }
        else
        {
            float d = f.Duration;
            if (ImGui.SliderFloat("Segundos", ref d, 0.1f, 30f)) f.Duration = d;
        }
        int r = (int)f.Rhythm;
        if (ImGui.Combo("Ritmo", ref r, RhythmNames, RhythmNames.Length)) f.Rhythm = (StrobeRhythm)r;
        int end = f.NextScene ? 1 : 0;
        if (ImGui.Combo("Al terminar", ref end, EndNames, EndNames.Length)) f.NextScene = end == 1;
        float maxHz = settings.AllowFastStrobe ? 20 : EffectDefs.SafeStrobeHz;
        float hz = Math.Min(f.Rate, maxHz);
        if (ImGui.SliderFloat("Strobe Hz", ref hz, 0.5f, maxHz, "%.1f")) f.Rate = hz;
        float duty = f.Duty;
        if (ImGui.SliderFloat("Strobe duración", ref duty, 0.05f, 0.95f)) f.Duty = duty;
        float inten = f.Intensity;
        if (ImGui.SliderFloat("Strobe intensidad", ref inten, 0f, 1f)) f.Intensity = inten;
        int mode = Math.Clamp(f.Mode, 0, EffectDefs.StrobeModes.Length - 1);
        if (ImGui.Combo("Strobe modo", ref mode, EffectDefs.StrobeModes, EffectDefs.StrobeModes.Length)) f.Mode = mode;
        if (mode == 3)
        {
            float hue = f.Hue;
            if (ImGui.SliderFloat("Strobe tono", ref hue, 0f, 1f)) f.Hue = hue;
            ImGui.TextDisabled("0 rojo · 0,33 verde · 0,66 azul");
        }
    }

    // ---- Ajustes ----
    void SettingsTab()
    {
        if (Section("Audio###setaudio", CDev)) { AudioBody(); EndSection(); }
        if (Section("Carpetas###folders", CFiles)) { FoldersBody(); EndSection(); }
        if (Section("Visor###viewer", CDev, false)) { ViewerBody(); EndSection(); }
        if (Section("Pantalla de salida###output", CDev, false)) { OutputSettings(); EndSection(); }
        if (Section("Control remoto###remote", CDev)) { RemoteSettings(); EndSection(); }
        if (Section("Strobe rápido###safety", CStrobe, false)) { FastStrobeBody(); EndSection(); }
        ImGui.TextDisabled("Proyectos en:");
        ImGui.TextWrapped(Settings.ProjectsDir);
    }

    void AudioBody()
    {
        int cur = sources.FindIndex(s => s.Id == capture.Current?.Id);
        var names = sources.Select(s => s.Name).ToArray();
        if (ImGui.Combo("Fuente", ref cur, names, names.Length))
        {
            capture.Start(sources[cur]);
            settings.AudioDeviceId = sources[cur].Id;
        }
        ImGui.SameLine();
        if (ImGui.Button("Actualizar")) sources = AudioCapture.List();

        float g = settings.InputGain;
        if (ImGui.SliderFloat("Ganancia de entrada", ref g, 0.1f, 10f, "x%.2f", ImGuiSliderFlags.Logarithmic)) settings.InputGain = g;
        float rel = settings.AudioRelease;
        if (ImGui.SliderFloat("Caída del audio", ref rel, 0.02f, 0.4f, "%.2f s", ImGuiSliderFlags.Logarithmic)) settings.AudioRelease = rel;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Cuánto tarda el nivel en bajar después de un golpe.\nMás bajo = baja a 0 entre golpes en temas rápidos.");
        ImGui.SeparatorText("Sensibilidad por banda");
        if (settings.BandSense is not { Length: 4 }) settings.BandSense = [1, 1, 1, 1];
        for (int b = 0; b < 4; b++)
        {
            float s = settings.BandSense[b];
            if (ImGui.SliderFloat($"{BandNames[b]}##sense", ref s, 0.1f, 10f, "x%.2f", ImGuiSliderFlags.Logarithmic)) settings.BandSense[b] = s;
            BandMeter(b);
        }
        ImGui.TextDisabled("Con música fuerte cada banda debería llegar cerca de 1 en los golpes.");
        if (ImGui.Button("Restablecer sensibilidad")) settings.BandSense = [1, 1, 1, 1];
        float ms = settings.MelodySmooth;
        if (ImGui.SliderFloat("Suavizado de melodía", ref ms, 0.1f, 3f, "%.2f s", ImGuiSliderFlags.Logarithmic)) settings.MelodySmooth = ms;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Cuántos segundos promedia la melodía.\nMás alto = se mueve más suave y lento.");
        if (capture.Error != null) Colored(Red, capture.Error);

        ImGui.SeparatorText("Enviar audio a la app del TV");
        bool net = settings.NetAudioEnabled;
        string url = settings.NetAudioUrl;
        if (ImGui.InputTextWithHint("Dirección del TV", "http://192.168.x.x:8765/?k=...", ref url, 200))
        {
            settings.NetAudioUrl = url;
            if (net) SetNetAudio(true);
        }
        if (ImGui.Checkbox("Enviar", ref net)) SetNetAudio(net);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("En el TV elegí la fuente \"Desde la PC (red)\". La dirección es la que aparece debajo del QR.");
        if (netError != null) Colored(Red, netError);
        else if (capture.Net is { } s) ImGui.TextDisabled(s.Error ?? $"Enviando a {s.Target}");
    }

    string? netError;

    void SetNetAudio(bool on)
    {
        settings.NetAudioEnabled = on;
        var old = capture.Net;
        capture.Net = null;
        old?.Dispose();
        netError = null;
        if (!on) return;
        try { capture.Net = new NetAudioSender(settings.NetAudioUrl); }
        catch (Exception e) { netError = e.Message; }
    }

    void FoldersBody()
    {
        string folder = settings.ShaderFolder;
        if (ImGui.InputText("Shaders", ref folder, 400, ImGuiInputTextFlags.EnterReturnsTrue)) SetShaderFolder(folder);
        ImGui.SameLine();
        if (ImGui.Button("Elegir...")) PickFolder(settings.ShaderFolder, "Carpeta con shaders .fs", SetShaderFolder);
        string media = settings.MediaFolder;
        if (ImGui.InputText("Videos", ref media, 400, ImGuiInputTextFlags.EnterReturnsTrue)) SetMediaFolder(media);
        ImGui.SameLine();
        if (ImGui.Button("Elegir...##media")) PickFolder(settings.MediaFolder, "Carpeta con videos e imágenes", SetMediaFolder);
        if (ImGui.Button("Recargar shaders y videos")) ReloadShaders();
        bool exp = settings.ExpandDefines;
        if (ImGui.Checkbox("Compatibilidad: expandir macros de los shaders (drivers viejos)", ref exp))
        {
            settings.ExpandDefines = IsfShader.ExpandDefines = exp;
            ReloadShaders();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Para placas que fallan o se cierran con #define que llevan parámetros (Intel HD viejas).\nNo cambia los archivos.");
    }

    static void PickFolder(string current, string title, Action<string> set)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath = current,
            Description = title,
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) set(dlg.SelectedPath);
    }

    void ViewerBody()
    {
        int si = Array.IndexOf(Scales, Scales.MinBy(s => MathF.Abs(s - settings.Scale)));
        if (ImGui.Combo("Resolución interna", ref si, ScaleNames, ScaleNames.Length)) { settings.Scale = Scales[si]; settings.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Del visor y de la ventana de salida. Más baja = más liviano; se estira al mostrarla.");
        var (iw, ih) = renderer.OutputSize;
        ImGui.TextDisabled($"Dibujando a {iw} x {ih}");
        bool fill = settings.FillWindow;
        if (ImGui.Checkbox("Llenar la ventana", ref fill)) settings.FillWindow = fill;
        if (fill) return;
        var fb = window.FramebufferSize;
        int vx = settings.ViewX, vy = settings.ViewY, vw = settings.ViewW, vh = settings.ViewH;
        if (ImGui.InputInt("X (px)", ref vx)) settings.ViewX = Math.Max(0, vx);
        if (ImGui.InputInt("Y (px)", ref vy)) settings.ViewY = Math.Max(0, vy);
        if (ImGui.InputInt("Ancho (px)", ref vw)) settings.ViewW = Math.Max(1, vw);
        if (ImGui.InputInt("Alto (px)", ref vh)) settings.ViewH = Math.Max(1, vh);
        ImGui.TextDisabled($"Ventana: {fb.X} x {fb.Y}. Lo que no entra se recorta.");
    }

    void FastStrobeBody()
    {
        bool fast = settings.AllowFastStrobe;
        if (ImGui.Checkbox("Permitir strobe rápido (más de 3 Hz)", ref fast))
        {
            if (fast) ImGui.OpenPopup("Aviso de strobe");
            else settings.AllowFastStrobe = false;
        }
        if (ImGui.BeginPopupModal("Aviso de strobe"))
        {
            ImGui.PushTextWrapPos(420);
            ImGui.TextWrapped("Los destellos de más de 3 por segundo pueden provocar crisis en personas con epilepsia fotosensible. Activalo solo si sabés que nadie del público está en riesgo.");
            ImGui.PopTextWrapPos();
            if (ImGui.Button("Activar")) { settings.AllowFastStrobe = true; ImGui.CloseCurrentPopup(); }
            ImGui.SameLine();
            if (ImGui.Button("Cancelar")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    static void Colored(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }
}
