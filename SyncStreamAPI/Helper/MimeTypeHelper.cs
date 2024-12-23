﻿using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Http;

namespace SyncStreamAPI.Helper;

public static class MimeTypeHelper
{
    private static readonly Dictionary<string, string> MimeTypeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".aac", "audio/aac" },
        { ".avi", "video/x-msvideo" },
        { ".csv", "text/csv" },
        { ".doc", "application/msword" },
        { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { ".gif", "image/gif" },
        { ".html", "text/html" },
        { ".jpeg", "image/jpeg" },
        { ".jpg", "image/jpeg" },
        { ".bmp", "image/bmp" },
        { ".tiff", "image/tiff" },
        { ".json", "application/json" },
        { ".mp3", "audio/mpeg" },
        { ".mpeg", "video/mpeg" },
        { ".png", "image/png" },
        { ".pdf", "application/pdf" },
        { ".ppt", "application/vnd.ms-powerpoint" },
        { ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
        { ".txt", "text/plain" },
        { ".wav", "audio/wav" },
        { ".weba", "audio/webm" },
        { ".webm", "video/webm" },
        { ".webp", "image/webp" },
        { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { ".xml", "application/xml" },
        { ".zip", "application/zip" }
        // Add more MIME types and their corresponding file extensions here
    };

    public static string GetMimeType(IFormFile file)
    {
        if (file == null) throw new ArgumentNullException(nameof(file));

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (MimeTypeMappings.TryGetValue(extension, out var mimeType)) return mimeType;

        // Default MIME type for unknown file extensions
        return "application/octet-stream";
    }
}