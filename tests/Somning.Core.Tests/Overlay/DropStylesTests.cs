using Somning.Core.Overlay;
using Somning.Core.Settings;
using Xunit;

namespace Somning.Core.Tests.Overlay;

public sealed class DropStylesTests
{
    [Fact]
    public void Classic_is_the_default_style_and_is_always_registered()
    {
        Assert.Equal(DropStyles.Classic, new UserSettings().DropStyle);
        Assert.Contains(DropStyles.All, style => style.Id == DropStyles.Classic);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("estilo-que-nao-existe")]
    public void Unknown_ids_fall_back_to_classic(string? id)
    {
        Assert.False(DropStyles.IsKnown(id));
        Assert.Equal(DropStyles.Classic, DropStyles.Normalize(id));
    }

    [Fact]
    public void Every_registered_style_has_a_unique_id_and_a_display_name()
    {
        Assert.Equal(DropStyles.All.Count, DropStyles.All.Select(style => style.Id).Distinct().Count());
        Assert.All(DropStyles.All, style => Assert.False(string.IsNullOrWhiteSpace(style.DisplayName)));
    }

    [Fact]
    public void The_settings_view_model_round_trips_the_selected_style_and_normalizes_unknown_ones()
    {
        var model = new SettingsViewModel(new UserSettings { DropStyle = "estilo-que-nao-existe" }, []);
        Assert.Equal(DropStyles.Classic, model.DropStyle);

        model.DropStyle = DropStyles.Classic;
        Assert.Equal(DropStyles.Classic, model.BuildUpdatedSettings().DropStyle);

        model.DropStyle = "outro-desconhecido";
        Assert.Equal(DropStyles.Classic, model.BuildUpdatedSettings().DropStyle);
    }
}
