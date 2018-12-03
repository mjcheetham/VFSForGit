﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using GVFS.Common;
using GVFS.Common.Tracing;

namespace GVFS.RepairJobs
{
    public class GitLocalBranchesRepairJob : GitRefsRepairJob
    {
        public GitLocalBranchesRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
        }

        public override string Name
        {
            get { return @"Local branches"; }
        }

        protected override IEnumerable<string> GetRefs()
        {
            string refsHeadsPath = Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Refs.Heads.RootFolder);

            IEnumerable<string> refsHeads = GetRecursiveRelativeFilePaths(refsHeadsPath);

            // Make sure we prepend "refs/heads" to the local branch name to get the full symbolic ref (relative to .git/)
            return refsHeads.Select(x => Path.Combine(GVFSConstants.DotGit.Refs.Name, GVFSConstants.DotGit.Refs.Heads.Name, x));
        }

        private static IEnumerable<string> GetRecursiveRelativeFilePaths(string rootDirectory)
        {
            return GetRecursiveRelativeFilePaths(new DirectoryInfo(rootDirectory), string.Empty);
        }

        private static IEnumerable<string> GetRecursiveRelativeFilePaths(DirectoryInfo directory, string prefix)
        {
            foreach (FileInfo file in directory.GetFiles())
            {
                yield return Path.Combine(prefix, file.Name);
            }

            foreach (DirectoryInfo childDirectory in directory.GetDirectories())
            {
                string childPrefix = Path.Combine(prefix, childDirectory.Name);

                foreach (string childFileName in GetRecursiveRelativeFilePaths(childDirectory, childPrefix))
                {
                    yield return childFileName;
                }
            }
        }
    }
}
