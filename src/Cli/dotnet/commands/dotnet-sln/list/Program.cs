// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.CommandLine.Parsing;
using System.Linq;
using Microsoft.DotNet.Cli;
using Microsoft.DotNet.Cli.Sln.Internal;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.Common;

namespace Microsoft.DotNet.Tools.Sln.List
{
    internal class ListProjectsInSolutionCommand : CommandBase
    {
        private readonly string _fileOrDirectory;
        private readonly bool _displaySolutionFolders;

        public ListProjectsInSolutionCommand(
            ParseResult parseResult) : base(parseResult)
        {
            _fileOrDirectory = parseResult.ValueForArgument<string>(SlnCommandParser.SlnArgument);
            _displaySolutionFolders = parseResult.ValueForOption(SlnListParser.SolutionFolderOption);
        }

        public override int Execute()
        {
            var slnFile = SlnFileFactory.CreateFromFileOrDirectory(_fileOrDirectory);

            string[] paths = slnFile.Projects
                .GetProjectsNotOfType(ProjectTypeGuids.SolutionFolderGuid)
                .Select(project => _displaySolutionFolders ? project.GetFullSolutionFolderPath() : project.FilePath)
                .ToArray();

            if (paths.Length == 0)
            {
                Reporter.Output.WriteLine(CommonLocalizableStrings.NoProjectsFound);
            }
            else
            {
                Array.Sort(paths);

                string header = _displaySolutionFolders ? LocalizableStrings.ProjectsSolutionFolderHeader : LocalizableStrings.ProjectsHeader;
                Reporter.Output.WriteLine($"{header}");
                Reporter.Output.WriteLine(new string('-', header.Length));
                foreach (string slnProject in paths)
                {
                    Reporter.Output.WriteLine(slnProject);
                }
            }
            return 0;
        }
    }
}
