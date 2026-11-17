using System.Numerics;
using ImGuiNET;
using Visual.Core;

namespace Visual.Desktop;

sealed partial class App
{
    static readonly string[] KindNames = ["Constante", "Lineal", "Audio"];
    static readonly string[] ModeNames = ["Reiniciar", "Rebotar"];
    static readonly string[] BandNames = ["Graves", "Medios", "Agudos"];
    static readonly Vector4 Red = new(1f, 0.45f, 0.45f, 1f);
    static readonly Vector4 Yellow = new(1f, 0.85f, 0.4f, 1f);

    List<AudioSource> sources = [];
    int selected = -1, addIndex;
    string status = "";

    void DrawUi()
    {
        ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(480, 700), ImGuiCond.FirstUseEver);
        ImGui.Begin("Visual   (F1 ocultar · F11 pantalla completa · N siguiente)");
        if (renderer.StrobeActive) Colored(Yellow, "STROBE");
        AudioPanel();
        ProjectPanel();
        ScenesPanel();
        SceneParamsPanel();
        EffectsPanel();
        AutoStrobePanel();
        SettingsPanel();
        if (status != "") ImGui.TextWrapped(status);
        ImGui.End();
    }

    void AudioPanel()
    {
        if (!ImGui.CollapsingHeader("Audio", ImGuiTreeNodeFlags.DefaultOpen)) return;
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
        for (int b = 0; b < 3; b++)
        {
            float v = analyzer.Bands.Raw[b];
            ImGui.ProgressBar(Math.Clamp(v, 0, 1), new Vector2(-1, 0), $"{BandNames[b]} {v:0.00}");
        }
        if (capture.Error != null) Colored(Red, capture.Error);
    }

    void ProjectPanel()
    {
        if (!ImGui.CollapsingHeader("Proyecto", ImGuiTreeNodeFlags.DefaultOpen)) return;
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

    void ScenesPanel()
    {
        if (!ImGui.CollapsingHeader("Escenas", ImGuiTreeNodeFlags.DefaultOpen)) return;
        var scenes = project.Scenes;
        for (int i = 0; i < scenes.Count; i++)
        {
            var s = scenes[i];
            var dur = s.Duration > 0 ? $"{s.Duration:0.#} s" : "manual";
            var mark = i == player.Current ? "> " : "   ";
            var fx = string.Join(" · ", EffectDefs.ActiveLabels(s.Effects));
            if (ImGui.Selectable($"{mark}{i + 1}. {s.Shader}   [{dur}]{(fx == "" ? "" : "   " + fx)}##scene{i}", i == selected)) selected = i;
            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) player.GoTo(i);
        }
        if (scenes.Count == 0) ImGui.TextDisabled("Sin escenas: agregá shaders abajo.");
        if (selected >= scenes.Count) selected = scenes.Count - 1;

        if (selected >= 0)
        {
            var s = scenes[selected];
            if (ImGui.Button("Reproducir")) player.GoTo(selected);
            ImGui.SameLine();
            if (ImGui.Button("Subir") && selected > 0) { player.Move(selected, selected - 1); selected--; }
            ImGui.SameLine();
            if (ImGui.Button("Bajar") && selected < scenes.Count - 1) { player.Move(selected, selected + 1); selected++; }
            ImGui.SameLine();
            if (ImGui.Button("Quitar")) player.Remove(selected);
            float d = s.Duration;
            if (ImGui.InputFloat("Duración (s, 0 = manual)", ref d, 1, 10, "%.1f")) s.Duration = Math.Max(0, d);
        }
        if (ImGui.Button("Siguiente escena (N)")) player.Next();

        ImGui.Separator();
        if (shaderFiles.Length == 0) ImGui.TextDisabled("No hay archivos .fs en la carpeta.");
        else
        {
            addIndex = Math.Clamp(addIndex, 0, shaderFiles.Length - 1);
            ImGui.Combo("Shader", ref addIndex, shaderFiles, shaderFiles.Length);
            ImGui.SameLine();
            if (ImGui.Button("Agregar"))
            {
                scenes.Add(new Scene { Shader = shaderFiles[addIndex] });
                selected = scenes.Count - 1;
            }
        }
        string folder = settings.ShaderFolder;
        if (ImGui.InputText("Carpeta", ref folder, 400, ImGuiInputTextFlags.EnterReturnsTrue)) SetShaderFolder(folder);
        ImGui.SameLine();
        if (ImGui.Button("Elegir...")) PickFolder();
        if (ImGui.Button("Recargar shaders")) ReloadShaders();
    }

    void PickFolder()
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath = settings.ShaderFolder,
            Description = "Carpeta con shaders .fs",
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) SetShaderFolder(dlg.SelectedPath);
    }

    void SceneParamsPanel()
    {
        if (selected < 0 || selected >= project.Scenes.Count) return;
        var scene = project.Scenes[selected];
        if (!ImGui.CollapsingHeader($"Parámetros: {scene.Shader}###params", ImGuiTreeNodeFlags.DefaultOpen)) return;
        var prog = renderer.Get(scene.Shader);
        if (prog.Error != null) Colored(Red, prog.Error);
        if (prog.Warning != null) Colored(Yellow, prog.Warning);
        if (prog.Isf.Description != "") ImGui.TextWrapped(prog.Isf.Description);
        prog.Isf.EnsureParams(scene.Params);
        double time = selected == player.Current ? player.SceneTime : 0;
        foreach (var i in prog.Isf.Inputs) InputEditor(i, scene.Params, time);
    }

    void InputEditor(IsfInput i, Dictionary<string, ParamState> ps, double time)
    {
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
                ImGui.TextDisabled($"{i.Label}: imágenes aún no soportadas");
                break;
        }
        ImGui.PopID();
    }

    void ParamEditor(string label, ParamState ps, float min, float max, string[]? labels, double time)
    {
        ImGui.PushID(label);
        float v = ps.Value[0];
        if (labels != null)
        {
            int idx = (int)MathF.Round(v);
            if (ImGui.Combo(label, ref idx, labels, labels.Length)) ps.Value[0] = idx;
            ImGui.PopID();
            return;
        }
        if (ImGui.SliderFloat(label, ref v, min, max)) ps.Value[0] = v;

        var m = ps.Mod;
        ImGui.Indent();
        int kind = (int)m.Kind;
        if (ImGui.Combo("Modificador", ref kind, KindNames, KindNames.Length)) m.Kind = (ModKind)kind;
        if (m.Kind == ModKind.Linear)
        {
            float sp = m.Speed;
            if (ImGui.SliderFloat("Velocidad (recorridos/s)", ref sp, 0.001f, 5f, "%.3f", ImGuiSliderFlags.Logarithmic)) m.Speed = sp;
            int mode = (int)m.Mode;
            if (ImGui.Combo("Al llegar al borde", ref mode, ModeNames, ModeNames.Length)) m.Mode = (LinearMode)mode;
        }
        else if (m.Kind == ModKind.Audio)
        {
            int band = (int)m.Band;
            if (ImGui.Combo("Banda", ref band, BandNames, BandNames.Length)) m.Band = (Band)band;
            float a = m.Amount;
            if (ImGui.SliderFloat("Intensidad", ref a, -1f, 1f)) m.Amount = a;
            float g = m.Gain;
            if (ImGui.SliderFloat("Ganancia", ref g, 0.1f, 10f, "x%.2f", ImGuiSliderFlags.Logarithmic)) m.Gain = g;
            float t = m.Threshold;
            if (ImGui.SliderFloat("Umbral", ref t, 0f, 0.95f)) m.Threshold = t;
            bool ag = m.AutoGain;
            if (ImGui.Checkbox("Auto-ganancia", ref ag)) m.AutoGain = ag;
        }
        if (m.Kind != ModKind.Constant)
        {
            float o = m.Apply(v, min, max, time, analyzer.Bands);
            ImGui.ProgressBar(max != min ? (o - min) / (max - min) : 0, new Vector2(-1, 0), o.ToString("0.###"));
        }
        ImGui.Unindent();
        ImGui.Spacing();
        ImGui.PopID();
    }

    static readonly Vector4 Green = new(0.4f, 0.9f, 0.5f, 1f);

    void EffectsPanel()
    {
        if (selected < 0 || selected >= project.Scenes.Count) return;
        var scene = project.Scenes[selected];
        if (!ImGui.CollapsingHeader($"Efectos de la escena {selected + 1}###effects")) return;
        scene.EnsureEffects();
        foreach (var d in EffectDefs.All)
        {
            var ps = scene.Effects[d.Name];
            bool en = !ps.Off;
            if (ImGui.Checkbox($"##on{d.Name}", ref en)) ps.Off = !en; // off keeps the settings
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(en ? "Desactivar sin perder la configuración" : "Activar");
            ImGui.SameLine();
            ActiveDot(EffectDefs.IsActive(d, ps));
            bool dim = ps.Off; // still editable, just dimmed
            if (dim) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.45f);
            ParamEditor(d.Label, ps, d.Min, d.Max, d.Labels, renderer.Time);
            if (dim) ImGui.PopStyleVar();
            if (d.Name == "strobeRate" && !settings.AllowFastStrobe)
                ImGui.TextDisabled("Limitado a 3 Hz (se cambia en Ajustes)");
        }
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

    static readonly string[] RhythmNames = ["Lineal (Strobe Hz)", "Sigue la banda"];
    static readonly string[] DurationNames = ["Fija", "Al azar"];
    static readonly string[] EndNames = ["Seguir en la escena", "Pasar a la siguiente"];

    static readonly string[] StrobeModeNames = ["Blanco", "Negro", "Invertir"];

    void AutoStrobePanel()
    {
        if (!ImGui.CollapsingHeader("Auto strobe")) return;
        var a = project.AutoStrobe;
        bool on = a.Enabled;
        if (ImGui.Checkbox("Activar auto strobe", ref on)) a.Enabled = on; // label must differ from the header's
        if (!on) return;
        var run = renderer.AutoStrobe;
        Colored(run.Active ? Yellow : run.Blocked ? Red : Green,
            run.Active ? "Disparando"
            : run.Blocked ? "Bloqueado: la escena que suena tiene su propio strobe (Strobe Hz > 0)"
            : run.CooldownLeft > 0 ? $"Enfriamiento: {run.CooldownLeft:0.0} s"
            : "Esperando que la banda supere el umbral");
        ImGui.PushID("auto");
        ImGui.Indent();
        int band = (int)a.Band;
        if (ImGui.Combo("Banda", ref band, BandNames, BandNames.Length)) a.Band = (Band)band;
        float g = a.Gain;
        if (ImGui.SliderFloat("Ganancia", ref g, 0.1f, 10f, "x%.2f", ImGuiSliderFlags.Logarithmic)) a.Gain = g;
        float t = a.Threshold;
        if (ImGui.SliderFloat("Umbral", ref t, 0.05f, 1f)) a.Threshold = t;
        float level = analyzer.Bands.Raw[(int)a.Band] * a.Gain;
        ImGui.ProgressBar(Math.Clamp(level, 0, 1), new Vector2(-1, 0), level >= a.Threshold ? "supera el umbral" : $"{level:0.00}");
        float p = a.Probability;
        if (ImGui.SliderFloat("Probabilidad", ref p, 0f, 100f, "%.0f %%")) a.Probability = p;

        int dm = a.RandomDuration ? 1 : 0;
        if (ImGui.Combo("Duración", ref dm, DurationNames, DurationNames.Length)) a.RandomDuration = dm == 1;
        if (a.RandomDuration)
        {
            float mn = a.MinDuration, mx = a.MaxDuration;
            if (ImGui.SliderFloat("Mínimo (s)", ref mn, 0.1f, 30f)) a.MinDuration = Math.Min(mn, a.MaxDuration);
            if (ImGui.SliderFloat("Máximo (s)", ref mx, 0.1f, 30f)) a.MaxDuration = Math.Max(mx, a.MinDuration);
        }
        else
        {
            float d = a.Duration;
            if (ImGui.SliderFloat("Segundos", ref d, 0.1f, 30f)) a.Duration = d;
        }
        float c = a.Cooldown;
        if (ImGui.SliderFloat("Enfriamiento (s)", ref c, 0f, 120f)) a.Cooldown = c;
        int r = (int)a.Rhythm;
        if (ImGui.Combo("Ritmo", ref r, RhythmNames, RhythmNames.Length)) a.Rhythm = (StrobeRhythm)r;
        int end = a.NextScene ? 1 : 0;
        if (ImGui.Combo("Al terminar", ref end, EndNames, EndNames.Length)) a.NextScene = end == 1;

        ImGui.SeparatorText("Destello");
        float maxHz = settings.AllowFastStrobe ? 20 : EffectDefs.SafeStrobeHz;
        float hz = Math.Min(a.Rate, maxHz); // also used by the phone's "hold to strobe" button
        if (ImGui.SliderFloat("Strobe Hz", ref hz, 0.5f, maxHz, "%.1f")) a.Rate = hz;
        float duty = a.Duty;
        if (ImGui.SliderFloat("Strobe duración", ref duty, 0.05f, 0.95f)) a.Duty = duty;
        float inten = a.Intensity;
        if (ImGui.SliderFloat("Strobe intensidad", ref inten, 0f, 1f)) a.Intensity = inten;
        int mode = Math.Clamp(a.Mode, 0, 2);
        if (ImGui.Combo("Strobe modo", ref mode, StrobeModeNames, StrobeModeNames.Length)) a.Mode = mode;
        ImGui.TextDisabled("No se dispara mientras la escena que suena tiene su propio strobe.");
        ImGui.Unindent();
        ImGui.PopID();
    }

    void SettingsPanel()
    {
        if (!ImGui.CollapsingHeader("Ajustes")) return;
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
        ImGui.Separator();
        bool fill = settings.FillWindow;
        if (ImGui.Checkbox("Visor: llenar la ventana", ref fill)) settings.FillWindow = fill;
        if (!fill)
        {
            var fb = window.FramebufferSize;
            int vx = settings.ViewX, vy = settings.ViewY, vw = settings.ViewW, vh = settings.ViewH;
            if (ImGui.InputInt("X (px)", ref vx)) settings.ViewX = Math.Max(0, vx);
            if (ImGui.InputInt("Y (px)", ref vy)) settings.ViewY = Math.Max(0, vy);
            if (ImGui.InputInt("Ancho (px)", ref vw)) settings.ViewW = Math.Max(1, vw);
            if (ImGui.InputInt("Alto (px)", ref vh)) settings.ViewH = Math.Max(1, vh);
            ImGui.TextDisabled($"Ventana: {fb.X} x {fb.Y}. Lo que no entra se recorta.");
        }
        RemoteSettings();
        ImGui.Separator();
        ImGui.TextDisabled("Proyectos en:");
        ImGui.TextWrapped(Settings.ProjectsDir);
    }

    static void Colored(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }
}
