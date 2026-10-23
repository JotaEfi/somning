using Somning.Core.Cloud;
using Somning.Core.Overlay;
using Xunit;

namespace Somning.Core.Tests.Overlay;

public sealed class OverlayNoticesTests
{
    [Theory]
    [InlineData(CloudErrorCodes.NoNetwork, OverlayNoticeKind.CloudNoNetwork)]
    [InlineData(CloudErrorCodes.InvalidApiKey, OverlayNoticeKind.CloudInvalidKey)]
    [InlineData(CloudErrorCodes.RateLimit, OverlayNoticeKind.CloudRateLimit)]
    [InlineData(CloudErrorCodes.Timeout, OverlayNoticeKind.CloudTimeout)]
    public void Cloud_error_codes_map_to_the_cloud_notice_kinds(string errorCode, OverlayNoticeKind expected)
    {
        Assert.Equal(expected, OverlayNotices.ForErrorCode(errorCode));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad_request")]
    [InlineData("server_error")]
    [InlineData("engine_failed")]
    public void Unclassified_codes_have_no_notice(string? errorCode)
    {
        Assert.Null(OverlayNotices.ForErrorCode(errorCode));
    }

    [Fact]
    public void Every_cloud_notice_has_the_required_pt_br_text()
    {
        Assert.Equal("Sem conexão com a internet", OverlayText.For(OverlayNoticeKind.CloudNoNetwork));
        Assert.Equal("Chave de API inválida", OverlayText.For(OverlayNoticeKind.CloudInvalidKey));
        Assert.Equal("Limite de requisições excedido", OverlayText.For(OverlayNoticeKind.CloudRateLimit));
        Assert.Equal("Tempo limite esgotado", OverlayText.For(OverlayNoticeKind.CloudTimeout));
    }
}
