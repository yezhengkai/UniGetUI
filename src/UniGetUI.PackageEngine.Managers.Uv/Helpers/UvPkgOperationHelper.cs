using UniGetUI.Core.Tools;
using UniGetUI.PackageEngine.Classes.Manager.BaseProviders;
using UniGetUI.PackageEngine.Enums;
using UniGetUI.PackageEngine.Interfaces;
using UniGetUI.PackageEngine.Managers.UvManager.Internal;
using UniGetUI.PackageEngine.Serializable;
using UniGetUI.PackageEngine.Structs;

namespace UniGetUI.PackageEngine.Managers.UvManager.Helpers;

internal sealed class UvPkgOperationHelper : BasePkgOperationHelper
{
    public UvPkgOperationHelper(Uv manager)
        : base(manager) { }

    protected override IReadOnlyList<string> _getOperationParameters(
        IPackage package,
        InstallOptions options,
        OperationType operation
    )
    {
        List<string> parameters =
        [
            operation switch
            {
                OperationType.Install => Manager.Properties.InstallVerb,
                OperationType.Update => Manager.Properties.UpdateVerb,
                OperationType.Uninstall => Manager.Properties.UninstallVerb,
                _ => throw new InvalidDataException("Invalid package operation"),
            },
        ];

        parameters.Add(
            options.Version.Any()
                ? CoreTools.EscapeCommandLineArgument($"{package.Id}=={options.Version}")
                : package.Id
        );

        if (operation is not OperationType.Uninstall)
        {
            OverridenInstallationOptions overrides = package.OverridenOptions;
            UvToolInstallRecord? recorded = operation is OperationType.Update
                ? UvToolInstallRecords.Get(package.Id)
                : null;

            string? pythonRequest = overrides.Uv_PythonRequest ?? recorded?.PythonRequest;
            bool? managedPython = overrides.Uv_ManagedPython ?? recorded?.ManagedPython;
            bool noPythonDownloads = overrides.Uv_NoPythonDownloads || (recorded?.NoPythonDownloads ?? false);

            if (pythonRequest is not null)
            {
                parameters.Add("--python");
                parameters.Add(CoreTools.EscapeCommandLineArgument(pythonRequest));
            }

            if (managedPython is bool managed)
                parameters.Add(managed ? "--managed-python" : "--no-managed-python");

            if (noPythonDownloads)
                parameters.Add("--no-python-downloads");
        }

        parameters.AddRange(
            operation switch
            {
                OperationType.Update => options.CustomParameters_Update,
                OperationType.Uninstall => options.CustomParameters_Uninstall,
                _ => options.CustomParameters_Install,
            }
        );

        return parameters;
    }

    protected override OperationVeredict _getOperationResult(
        IPackage package,
        OperationType operation,
        IReadOnlyList<string> processOutput,
        int returnCode
    )
    {
        if (returnCode != 0)
            return OperationVeredict.Failure;

        switch (operation)
        {
            case OperationType.Install or OperationType.Update:
                {
                    OverridenInstallationOptions overrides = package.OverridenOptions;
                    UvToolInstallRecord? recorded = overrides.Uv_PythonRequest is null
                        ? UvToolInstallRecords.Get(package.Id)
                        : null;

                    string? pythonRequest = overrides.Uv_PythonRequest ?? recorded?.PythonRequest;
                    UvToolInstallRecords.Record(
                        new UvToolInstallRecord
                        {
                            Id = package.Id,
                            Version = optionsVersionFor(package),
                            PythonRequest = pythonRequest,
                            ManagedPython = overrides.Uv_ManagedPython ?? recorded?.ManagedPython,
                            NoPythonDownloads = overrides.Uv_NoPythonDownloads
                                || (recorded?.NoPythonDownloads ?? false),
                            InstalledAt = DateTime.UtcNow.ToString("o"),
                            Source = package.Source.Name,
                            SchemaVersion = UvToolInstallRecords.SchemaVersion,
                        }
                    );
                    break;
                }
            case OperationType.Uninstall:
                UvToolInstallRecords.Forget(package.Id);
                break;
        }

        return OperationVeredict.Success;
    }

    private static string optionsVersionFor(IPackage package)
    {
        if (package.NewVersionString is { } newer && newer.Length > 0)
            return newer;
        return package.VersionString;
    }
}
