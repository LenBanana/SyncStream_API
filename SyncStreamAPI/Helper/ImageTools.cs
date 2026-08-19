using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.DTOModel;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.PostgresModels;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace SyncStreamAPI.Helper;

public class ImageTools
{
    [ErrorHandling]
    public static async Task<ActionResult> ConvertFile(IFormFile file, MediaType mediaType, DbUser dbUser,
        PostgresContext postgresContext, IHubContext<ServerHub, IServerHub> serverHub)
    {
        IImageEncoder encoder = null;
        var mimeType = "image/png";
        var fileName = file.FileName;
        fileName = Path.ChangeExtension(fileName, $".{mediaType.ToString().ToLower()}");
        var fileInfo = new FileInfo(fileName);
        switch (mediaType)
        {
            case MediaType.PNG:
                encoder = new PngEncoder();
                break;
            case MediaType.JPEG:
                encoder = new JpegEncoder();
                mimeType = "image/jpeg";
                break;
            case MediaType.BMP:
                encoder = new BmpEncoder();
                mimeType = "image/bmp";
                break;
            case MediaType.WEBP:
                encoder = new WebpEncoder();
                mimeType = "image/webp";
                break;
            case MediaType.TIFF:
                encoder = new TiffEncoder();
                mimeType = "image/tiff";
                break;
        }

        var dbFile = new DbFile(Path.GetFileNameWithoutExtension(fileName), fileInfo.Extension, dbUser, true,
            DateTime.UtcNow.AddMinutes(General.MinutesToKeepFFmpeg.Minutes));
        var outputPath = Path.Combine(General.TemporaryFilePath, $"{dbFile.FileKey}{dbFile.FileEnding}");
        if (mediaType == MediaType.PDF)
            await ConvertToPdf(outputPath, file);
        else
            using (var image = await Image.LoadAsync(file.OpenReadStream()))
            {
                await image.SaveAsync(outputPath, encoder);
            }

        await SaveToDb(dbFile, postgresContext);
        var fileBytes = await File.ReadAllBytesAsync(outputPath);
        await serverHub.Clients.Group(dbUser.ApiKey).updateFolders(new FileDto(dbFile));
        var result = new FileContentResult(fileBytes, mimeType)
        {
            FileDownloadName = $"{dbFile.Name}{dbFile.FileEnding}"
        };
        return result;
    }

    private static async Task SaveToDb(DbFile dbFile, PostgresContext postgresContext)
    {
        postgresContext.Files?.Add(dbFile);
        await postgresContext.SaveChangesAsync();
    }

    /// <summary>
    ///     Writes the uploaded image onto a single PDF page sized to the image.
    /// </summary>
    /// <remarks>
    ///     The upload is normalised to PNG with ImageSharp first, so the PDF writer only ever has to
    ///     handle one format and every input ImageSharp can read is supported.
    /// </remarks>
    private static async Task ConvertToPdf(string outputPath, IFormFile file)
    {
        if (file.Length <= 0) return;

        using var pngStream = new MemoryStream();
        using (var image = await Image.LoadAsync(file.OpenReadStream()))
        {
            await image.SaveAsync(pngStream, new PngEncoder());
        }

        pngStream.Position = 0;

        using var pdfImage = XImage.FromStream(pngStream);
        using var pdfDocument = new PdfDocument();
        var page = pdfDocument.AddPage();
        page.Width = XUnit.FromPoint(pdfImage.PointWidth);
        page.Height = XUnit.FromPoint(pdfImage.PointHeight);

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawImage(pdfImage, 0, 0, page.Width.Point, page.Height.Point);
        }

        pdfDocument.Save(outputPath);
    }
}