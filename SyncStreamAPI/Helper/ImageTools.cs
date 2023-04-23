using Microsoft.AspNetCore.Http;
using System.IO;

namespace SyncStreamAPI.Helper
{
    public class ImageTools
    {
        //public async static void ConvertFile(IFormFile file)
        //{

        //    using (var inputStream = new MemoryStream())
        //    using (var outputStream = new MemoryStream())
        //    {
        //        await file.CopyToAsync(inputStream);

        //        // Use ImageSharp library to convert the image to the new format
        //        var image = Image.Load(inputStream);
        //        image.Save(outputStream, GetImageFormat(newImageType));

        //        // Return the converted image as a File Content Result
        //        return File(outputStream.ToArray(), GetContentType(newImageType));
        //    }
        //}
    }
}
