using Somning.Core.Overlay;
using Xunit;

namespace Somning.Core.Tests.Overlay;

public sealed class OverlayViewModelTests
{
    [Fact]
    public void Default_view_model_is_hidden_and_empty()
    {
        var model = new OverlayViewModel();

        Assert.Equal(OverlayMode.Hidden, model.Mode);
        Assert.Null(model.Text);
        Assert.Null(model.Message);
        Assert.Null(model.Notice);
        Assert.Null(model.NoticeKind);
        Assert.NotNull(model.Bars);
        Assert.Empty(model.Bars);
    }

    [Fact]
    public void CreateHidden_produces_the_idle_snapshot()
    {
        var model = OverlayViewModel.CreateHidden();

        Assert.Equal(OverlayMode.Hidden, model.Mode);
        Assert.Null(model.Message);
        Assert.Empty(model.Bars);
        Assert.Null(model.NoticeKind);
    }

    [Fact]
    public void CreateListening_exposes_the_pt_br_text_and_the_bars()
    {
        var bars = new[] { 0.15f, 0.5f, 0.9f, 0.5f, 0.15f };

        var model = OverlayViewModel.CreateListening(bars);

        Assert.Equal(OverlayMode.Listening, model.Mode);
        Assert.Equal("Ouvindo...", model.Text);
        Assert.Equal("Ouvindo...", model.Message);
        Assert.Equal(OverlayText.Listening, model.Text);
        Assert.Equal(bars, model.Bars);
        Assert.Null(model.Notice);
        Assert.Null(model.NoticeKind);
    }

    [Fact]
    public void CreateListening_copies_the_bars_so_later_mutations_do_not_leak()
    {
        var bars = new[] { 0.15f, 0.5f, 0.9f };

        var model = OverlayViewModel.CreateListening(bars);
        bars[0] = 1f;
        bars[1] = 1f;
        bars[2] = 1f;

        Assert.Equal(new[] { 0.15f, 0.5f, 0.9f }, model.Bars);
        Assert.NotSame(bars, model.Bars);
    }

    [Fact]
    public void CreateListening_tolerates_a_null_bars_reference()
    {
        var model = OverlayViewModel.CreateListening(null!);

        Assert.Equal(OverlayMode.Listening, model.Mode);
        Assert.NotNull(model.Bars);
        Assert.Empty(model.Bars);
    }

    [Fact]
    public void CreateTranscribing_exposes_the_pt_br_text_without_bars()
    {
        var model = OverlayViewModel.CreateTranscribing();

        Assert.Equal(OverlayMode.Transcribing, model.Mode);
        Assert.Equal("Transcrevendo...", model.Text);
        Assert.Equal("Transcrevendo...", model.Message);
        Assert.Empty(model.Bars);
        Assert.Null(model.Notice);
    }

    [Theory]
    [InlineData(OverlayNoticeKind.MicMissing, "Microfone não encontrado")]
    [InlineData(OverlayNoticeKind.MicBlocked, "Microfone bloqueado")]
    [InlineData(OverlayNoticeKind.SilenceOnly, "Apenas silêncio detectado")]
    [InlineData(OverlayNoticeKind.NothingDetected, "Nenhuma fala detectada")]
    [InlineData(OverlayNoticeKind.ModelMissing, "Modelo de transcrição ausente")]
    [InlineData(OverlayNoticeKind.CloudNoNetwork, "Sem conexão com a internet")]
    [InlineData(OverlayNoticeKind.CloudInvalidKey, "Chave de API inválida")]
    [InlineData(OverlayNoticeKind.CloudRateLimit, "Limite de requisições excedido")]
    [InlineData(OverlayNoticeKind.CloudTimeout, "Tempo limite esgotado")]
    public void CreateNotice_maps_every_kind_to_the_required_pt_br_text(
        OverlayNoticeKind kind,
        string expected)
    {
        var model = OverlayViewModel.CreateNotice(kind);

        Assert.Equal(OverlayMode.Notice, model.Mode);
        Assert.Equal(expected, model.Text);
        Assert.Equal(expected, model.Message);
        Assert.Equal(kind, model.Notice);
        Assert.Equal(kind, model.NoticeKind);
        Assert.Empty(model.Bars);
    }

    [Fact]
    public void OverlayText_maps_every_declared_notice_kind()
    {
        foreach (var kind in Enum.GetValues<OverlayNoticeKind>())
        {
            var text = OverlayText.For(kind);

            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Fact]
    public void OverlayText_rejects_an_unknown_notice_kind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayText.For((OverlayNoticeKind)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayViewModel.CreateNotice((OverlayNoticeKind)99));
    }

    [Fact]
    public void With_expression_produces_a_new_snapshot_without_mutating_the_original()
    {
        var listening = OverlayViewModel.CreateListening([0.2f, 0.4f]);

        var notice = listening with { Mode = OverlayMode.Notice, Text = "Microfone bloqueado" };

        Assert.NotSame(listening, notice);
        Assert.Equal(OverlayMode.Listening, listening.Mode);
        Assert.Equal("Ouvindo...", listening.Text);
        Assert.Equal(OverlayMode.Notice, notice.Mode);
        Assert.Equal("Microfone bloqueado", notice.Message);
        Assert.Equal(listening.Bars, notice.Bars);
    }

    [Fact]
    public void Constructor_stores_the_supplied_state()
    {
        var model = new OverlayViewModel(OverlayMode.Notice, "Aviso", [1f], OverlayNoticeKind.ModelMissing);

        Assert.Equal(OverlayMode.Notice, model.Mode);
        Assert.Equal("Aviso", model.Text);
        Assert.Equal(new[] { 1f }, model.Bars);
        Assert.Equal(OverlayNoticeKind.ModelMissing, model.NoticeKind);
    }

    [Fact]
    public void Enums_expose_the_modes_and_notice_kinds_required_by_the_overlay()
    {
        Assert.Equal(
            new[]
            {
                OverlayMode.Hidden,
                OverlayMode.Listening,
                OverlayMode.Transcribing,
                OverlayMode.Notice
            },
            Enum.GetValues<OverlayMode>());

        Assert.Equal(
            new[]
            {
                OverlayNoticeKind.MicMissing,
                OverlayNoticeKind.MicBlocked,
                OverlayNoticeKind.SilenceOnly,
                OverlayNoticeKind.NothingDetected,
                OverlayNoticeKind.ModelMissing,
                OverlayNoticeKind.CloudNoNetwork,
                OverlayNoticeKind.CloudInvalidKey,
                OverlayNoticeKind.CloudRateLimit,
                OverlayNoticeKind.CloudTimeout,
                OverlayNoticeKind.EngineFailed,
                OverlayNoticeKind.TranscribeTimeout
            },
            Enum.GetValues<OverlayNoticeKind>());
    }
}
