using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MediaEntity = CallingBell.Domain.Entities.Media;

namespace CallingBell.Application.Features.Media;

public sealed record MediaFileDto(byte[] Data, string ContentType, string FileName, DateTimeOffset LastModified);

public sealed record GetMediaFileQuery(Guid MediaId, bool Thumbnail) : IRequest<MediaFileDto>;

public sealed class GetMediaFileHandler(IUnitOfWork uow) : IRequestHandler<GetMediaFileQuery, MediaFileDto>
{
    public async Task<MediaFileDto> Handle(GetMediaFileQuery request, CancellationToken ct)
    {
        var file = await uow.Repository<MediaEntity>().QueryNoTracking()
            .Where(m => m.MediaId == request.MediaId && m.IsActive)
            .Select(m => new
            {
                Data = request.Thumbnail && m.ThumbnailData != null ? m.ThumbnailData : m.FileData,
                m.ContentType, m.FileName, m.CreatedOn, m.ModifiedOn
            })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Media", request.MediaId);

        return new MediaFileDto(file.Data, file.ContentType, file.FileName, file.ModifiedOn ?? file.CreatedOn);
    }
}
