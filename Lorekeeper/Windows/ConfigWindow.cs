using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Lorekeeper.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private const int ApiKeyInputCapacity = 512;
    private const int ModelInputCapacity = 100;

    private static readonly Vector2 InitialWindowSize = new(520.0f, 230.0f);
    private static readonly Vector2 SaveButtonSize = new(160.0f, 0.0f);
    private static readonly Vector2 ClearKeyButtonSize = new(130.0f, 0.0f);

    private readonly Configuration configuration;

    private string apiKey;
    private string model;

    public ConfigWindow(Plugin plugin)
        : base("Lorekeeper — Ustawienia###LorekeeperConfig")
    {
        configuration = plugin.Configuration;
        apiKey = configuration.OpenAiApiKey;
        model = configuration.OpenAiModel;

        Size = InitialWindowSize;
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
        SetWindowMovability(configuration.IsConfigWindowMovable);
    }

    public override void Draw()
    {
        DrawIntroduction();
        DrawSectionSeparator();
        DrawOpenAiSettings();
        DrawWindowSettings();
        DrawSectionSeparator();
        DrawActions();
    }

    private static void DrawIntroduction()
    {
        ImGui.TextWrapped(
            "Wprowadź klucz OpenAI API. Klucz zostanie zapisany lokalnie " +
            "w konfiguracji pluginu.");
    }

    private void DrawOpenAiSettings()
    {
        ImGui.Text("OpenAI API Key");
        ImGui.SetNextItemWidth(-1.0f);
        ImGui.InputText(
            "##OpenAiApiKey",
            ref apiKey,
            ApiKeyInputCapacity,
            ImGuiInputTextFlags.Password);

        ImGui.Spacing();

        ImGui.Text("Model");
        ImGui.SetNextItemWidth(-1.0f);
        ImGui.InputText(
            "##OpenAiModel",
            ref model,
            ModelInputCapacity);

        ImGui.Spacing();
    }

    private void DrawWindowSettings()
    {
        bool isMovable = configuration.IsConfigWindowMovable;

        if (!ImGui.Checkbox("Pozwól przesuwać okno", ref isMovable))
        {
            return;
        }

        configuration.IsConfigWindowMovable = isMovable;
    }

    private void DrawActions()
    {
        if (ImGui.Button("Zapisz ustawienia", SaveButtonSize))
        {
            SaveSettings();
        }

        ImGui.SameLine();

        if (ImGui.Button("Wyczyść klucz", ClearKeyButtonSize))
        {
            ClearApiKey();
        }
    }

    private void SaveSettings()
    {
        configuration.OpenAiApiKey = apiKey.Trim();
        configuration.OpenAiModel = model.Trim();
        configuration.Save();
    }

    private void ClearApiKey()
    {
        apiKey = string.Empty;
        configuration.OpenAiApiKey = string.Empty;
        configuration.Save();
    }

    private void SetWindowMovability(bool isMovable)
    {
        if (isMovable)
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
            return;
        }

        Flags |= ImGuiWindowFlags.NoMove;
    }

    private static void DrawSectionSeparator()
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }
}
