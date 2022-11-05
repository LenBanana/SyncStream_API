using SyncStreamAPI.PostgresModels;
using System.Collections.Generic;
using System.Linq;

namespace SyncStreamAPI.DTOModel
{
    public class FolderDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public FolderDto? Parent { get; set; }
        public List<FolderDto> Children { get; set; }
        public List<FileDto> Files { get; set; }
        public FolderDto(DbFileFolder folder)
        {
            Id = folder.Id;
            Name = folder.Name;
            if (folder.Parent != null)
                Parent = new FolderDto(folder.Parent.Id, folder.Parent.Name);
            Children = folder.Children?.Select(x => new FolderDto(x)).ToList();
            Files = folder.Files?.Select(x => new FileDto(x)).ToList();
        }
        public FolderDto(int id, string name)
        {
            Id = id;
            Name = name;
        }
    }
}
