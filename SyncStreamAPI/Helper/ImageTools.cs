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
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.PostgresModels;
using System;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper
{
    public class ImageTools
    {
        public static async Task<ActionResult> ConvertFile(IFormFile file, MediaType mediaType, DbUser dbUser, PostgresContext postgresContext, IHubContext<ServerHub, IServerHub> serverHub)
        {
            try
            {
                IImageEncoder encoder = null;
                string mimeType = "image/png";
                var fileName = file.FileName;
                fileName = System.IO.Path.ChangeExtension(fileName, $".{mediaType.ToString().ToLower()}");
                var fileInfo = new System.IO.FileInfo(fileName);
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
                var dbFile = new DbFile(System.IO.Path.GetFileNameWithoutExtension(fileName), fileInfo.Extension, dbUser, temporary: true, DateTime.UtcNow.AddMinutes(General.MinutesToKeepFFmpeg.Minutes));
                var outputPath = System.IO.Path.Combine(General.TemporaryFilePath, $"{dbFile.FileKey}{dbFile.FileEnding}");
                using (var image = Image.Load(file.OpenReadStream()))
                {
                    image.Save(outputPath, encoder);
                }
                var savedFile = postgresContext.Files?.Add(dbFile);
                await postgresContext.SaveChangesAsync();
                var fileBytes = await System.IO.File.ReadAllBytesAsync(outputPath);
                await serverHub.Clients.Group(dbUser.ApiKey).updateFolders(new DTOModel.FileDto(savedFile.Entity));
                FileContentResult result = new FileContentResult(fileBytes, mimeType);
                result.FileDownloadName = $"{dbFile.Name}{dbFile.FileEnding}";
                return result;
            }
            catch (Exception ex)
            {
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
