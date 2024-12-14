using System;
using System.IO;
using System.Threading.Tasks;
using iText.IO.Image;
using iText.Kernel.Pdf;
using iText.Layout;
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
            using (var image = Image.Load(file.OpenReadStream()))
            {
                image.Save(outputPath, encoder);
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
        var savedFile = postgresContext.Files?.Add(dbFile);
        await postgresContext.SaveChangesAsync();
    }

    private static async Task ConvertToPdf(string outputPath, IFormFile file)
    {
        if (file.Length > 0)
        {
            var pdfDocument = new PdfDocument(new PdfWriter(outputPath));
            var document = new Document(pdfDocument);
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                var fileBytes = ms.ToArray();
                var imageData = ImageDataFactory.Create(fileBytes);
                var image = new iText.Layout.Element.Image(imageData);
                document.Add(image);
            }

            document.Close();
        }
    }
}