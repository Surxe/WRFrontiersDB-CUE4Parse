using System.Threading;
using Spectre.Console.Cli;

namespace BatchExport.Cli
{
    /// <summary>
    /// The one command: export the War Robots: Frontiers assets described by WrfConstants.
    /// </summary>
    public sealed class ExportCommand : Command<ExportSettings>
    {
        public override int Execute(CommandContext context, ExportSettings settings, CancellationToken cancellationToken)
        {
            return BatchExportProgram.Export(settings);
        }
    }
}
