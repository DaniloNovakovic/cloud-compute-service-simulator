﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Compute
{
    internal static class Program
    {
        private static readonly PackageManager packageManager = new PackageManager();
        private static readonly ProcessManager processManager = ProcessManager.Instance;

        private static void Main()
        {
            var configItem = LoadComputeConfiguration();
            processManager.StartContainerProcesses(configItem);

            try
            {
                var validPackage = StartPeriodicCheckUntilFirstValidPackageIsFound(configItem);
                var destinationAssemblies = CopyAssemblies(configItem, validPackage);
                SendLoadAssemblySignalToContainers(destinationAssemblies);
                StartPeriodHealthChecksInTheBackground(destinationAssemblies);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
            }

            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();

            processManager.StopAllProcesses();
        }

        private static void StartPeriodHealthChecksInTheBackground(List<AssemblyInfo> destinationAssemblies)
        {
            Console.WriteLine("Running periodic health checks in the background... ");
            foreach (var assembly in destinationAssemblies)
            {
                Task.Factory.StartNew((object tempAssembly) =>
                {
                    ContainerController.StartPeriodicHealthCheck((AssemblyInfo)tempAssembly, OnContainerFailure);
                }, assembly.Clone());
            }
        }

        private static bool OnContainerFailure(AssemblyInfo assembly, Exception exception)
        {
            Console.Error.WriteLine("Container failed/has closed... Exception msg: " + exception.Message);
            return true;
        }

        private static void SendLoadAssemblySignalToContainers(List<AssemblyInfo> destinationPaths)
        {
            Console.WriteLine("Sending load assembly signal to requested number of container processes...");

            ContainerController.SendLoadSignalToContainers(destinationPaths).GetAwaiter().GetResult();

            Console.WriteLine("All of the processes have finished loading assemblies");
        }

        private static List<AssemblyInfo> CopyAssemblies(ComputeConfigurationItem configItem, PackageReaderResult package)
        {
            Console.WriteLine($"Copying assemblies to n={package.NumberOfInstances} destinations...");

            var ports = ProcessManager.Instance.GetAllContainerPorts().Take(package.NumberOfInstances ?? 0).ToList();
            string sourceDllFullPath = Path.Combine(configItem.PackageFullFolderPath, package.AssemblyName);
            return packageManager.CopyAssemblies(sourceDllFullPath, configItem.PackageFullFolderPath, ports);
        }

        private static ComputeConfigurationItem LoadComputeConfiguration()
        {
            Console.WriteLine("Loading Compute configuration...");

            var configItem = ComputeConfiguration.Instance.ConfigurationItem;
            Debug.WriteLine(configItem);
            return configItem;
        }

        private static PackageReaderResult StartPeriodicCheckUntilFirstValidPackageIsFound(ComputeConfigurationItem configItem)
        {
            Console.WriteLine("Starting periodic check until first valid package is found...");

            var package = packageManager.PeriodicallyCheckForValidPackage(configItem);
            Debug.WriteLine(package);
            return package;
        }
    }
}