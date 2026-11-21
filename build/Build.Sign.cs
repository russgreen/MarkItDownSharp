using Fallout.Common;
using Fallout.Common.Git;
using Fallout.Common.IO;
using Fallout.Common.Tools.SignTool;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using static Fallout.Common.Tools.SignTool.SignToolTasks;

partial class Build
{
    Target Sign => _ => _
        .TriggeredBy(Publish)
        .OnlyWhenStatic(() => GitRepository.IsOnMainOrMasterBranch())
        .Executes(() =>
        {
            // Only our own binaries are signed; the .NET and Windows App SDK files already carry Microsoft signatures.
            var files = PublishDirectory
                .GlobFiles("MarkItDownSharp*.dll", "MarkItDownSharp*.exe")
                .Select(file => file.ToString())
                .ToList();

            if (files.Count == 0)
            {
                throw new InvalidOperationException($"No MarkItDownSharp binaries were found in {PublishDirectory} to sign.");
            }

            foreach (var file in files)
            {
                Log.Information("File : {file}", file);
            }

            SignFiles(files);
        });

    static void SignFiles(List<string> files) => SignTool(s => s
        .SetFileDigestAlgorithm("sha256")
        .SetTimestampServerUrl("http://time.certum.pl")
        .SetSigningSubjectName("Open Source Developer, Russell Green")
        .SetFiles(files.ToArray()));
}
