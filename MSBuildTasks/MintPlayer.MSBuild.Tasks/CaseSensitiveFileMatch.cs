using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
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
            Exists = System.IO.Directory.EnumerateFiles(Directory, Path.GetFileName(Candidate))
                .Any(file => string.Equals(Path.GetFileName(file), Path.GetFileName(Candidate), System.StringComparison.Ordinal));

            var item = new TaskItem(Path.Combine(Directory, Candidate));
            item.SetMetadata("BaseName", Candidate);
            item.SetMetadata("Exists", "true");
            MatchedFiles = new[] { item };

            return true;
        }
    }
}

