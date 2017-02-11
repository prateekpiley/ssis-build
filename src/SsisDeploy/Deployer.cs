﻿using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using SsisBuild.Core;
using SsisBuild.Logger;

namespace SsisDeploy
{
    public class Deployer : IDeployer
    {
        private readonly ILogger _logger;
        private readonly IProject _project;
        private readonly ICatalogTools _catalogTools;

        internal Deployer(ILogger logger, IProject project, ICatalogTools catalogTools)
        {
            _logger = logger;
            _project = project;
            _catalogTools = catalogTools;
        }

        public Deployer() : this(new ConsoleLogger(), new Project(), new CatalogTools()) { }

        public void Deploy(IDeployArguments deployArguments)
        {
            _project.LoadFromIspac(deployArguments.DeploymentFilePath, deployArguments.ProjectPassword);

            var parametersToDeploy = _project.Parameters.Where(p => p.Value.Sensitive && p.Value.Value != null)
                .ToDictionary(p => string.Copy(p.Key), v => new SensitiveParameter()
                {
                    DataType = v.Value.ParameterDataType,
                    Name = string.Copy(v.Key),
                    Value = string.Copy(v.Value.Value)
                });


            var deploymentProtectionLevel = deployArguments.EraseSensitiveInfo ? ProtectionLevel.DontSaveSensitive : ProtectionLevel.ServerStorage;

            LogDeploymentInfo(deployArguments, parametersToDeploy, deploymentProtectionLevel);

            var connectionString = new SqlConnectionStringBuilder()
            {
                ApplicationName = "SSIS Deploy",
                DataSource = deployArguments.ServerInstance,
                InitialCatalog = deployArguments.Catalog,
                IntegratedSecurity = true
            }.ConnectionString;

            using (var zipStream = new MemoryStream())
            {
                _project.Save(zipStream, deploymentProtectionLevel, deployArguments.ProjectPassword);
                zipStream.Flush();

                _catalogTools.DeployProject(connectionString, deployArguments.Folder, deployArguments.ProjectName, deployArguments.EraseSensitiveInfo, parametersToDeploy, zipStream);
            }

        }

        private void LogDeploymentInfo(IDeployArguments deployArguments, Dictionary<string, SensitiveParameter> parametersToDeploy, ProtectionLevel deploymentProtectionLevel)
        {
            _logger.LogMessage("SSIS Deploy Engine");
            _logger.LogMessage("Copyright (c) 2017 Roman Tumaykin");
            _logger.LogMessage("");
            _logger.LogMessage("-------------------------------------------------------------------------------");
            _logger.LogMessage("Starting SSIS Project deployment with the following parameters:");
            _logger.LogMessage("");
            _logger.LogMessage($"Project path:         {deployArguments.DeploymentFilePath}.");
            _logger.LogMessage($"Target SQL Server:    {deployArguments.ServerInstance}");
            _logger.LogMessage($"Target IS Catalog:    {deployArguments.Catalog}");
            _logger.LogMessage($"Target Project Name:  {deployArguments.ProjectName}");
            _logger.LogMessage($"Protection Level:     {deploymentProtectionLevel:G}");

            if (parametersToDeploy.Count > 0)
            {
                _logger.LogMessage("");
                _logger.LogMessage("The following parameters will be deployed together with the project:");
                foreach (var parameter in parametersToDeploy)
                {
                    _logger.LogMessage($"    - {parameter.Key};");
                }
            }
        }
    }
}