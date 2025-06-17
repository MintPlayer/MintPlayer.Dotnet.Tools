using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.IO;
using System.Linq;

namespace MintPlayer.MSBuild.Tasks
{
    public class CaseSensitiveFileMatch : Microsoft.Build.Utilities.Task
    {
        [Required]
        public string Directory { get; set; }

        [Required]
        public string Candidate { get; set; }

        [Output]
        public bool Exists { get; set; }

        [Output]
        public ITaskItem[] MatchedFiles { get; set; }

        public override bool Execute()
        {
            var targetFileName = Path.GetFileName(Candidate);
            var matchedFilePath = System.IO.Directory.EnumerateFiles(Directory, targetFileName)
                .FirstOrDefault(file => string.Equals(Path.GetFileName(file), targetFileName, StringComparison.Ordinal));

            if (matchedFilePath != null)
            {
                Exists = true;
                var item = new TaskItem(matchedFilePath);
                item.SetMetadata("BaseName", targetFileName);
                item.SetMetadata("Exists", "true");
                MatchedFiles = new[] { item };
            }
            else
            {
                Exists = false;
                MatchedFiles = Array.Empty<ITaskItem>();
            }

            return true;
        }
    }
}

