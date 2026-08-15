using TrackZ.Application.Media.RequestUpload;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Contracts.Errors;

namespace TrackZ.Application.Tests.Media;

public sealed class ImageUploadTests
{
    [Theory]
    [InlineData("image/gif", 1024, BusinessErrorCode.ImageTypeNotSupported)]
    [InlineData("image/jpeg", 0, BusinessErrorCode.InvalidRequest)]
    [InlineData("image/jpeg", 5_000_001, BusinessErrorCode.ImageTooLarge)]
    public async Task Request_rejects_sensitive_invalid_metadata(string contentType, long length, BusinessErrorCode expectedCode)
    {
        var handler = new RequestImageUploadHandler();

        var error = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(new RequestImageUploadCommand(Guid.NewGuid(), contentType, length), CancellationToken.None));

        Assert.Equal(expectedCode, error.Code);
    }
}
