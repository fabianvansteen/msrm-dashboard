﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using Dapper;
using RMDashboard.Models;
using RMDashboard.Repositories;
using RMDashboard.Validators;
using System.Reflection;
using System.Diagnostics;

namespace RMDashboard.Controllers
{
    /// <summary>
    /// WebAPI controller that retreives all dashboard data.
    /// </summary>
    public class ReleasesController : ApiController
    {
        private IReleaseRepository _releaseRepository;

        private const string URL_RELEASE_EXPLORER_KEY = "UrlReleaseExplorer";

        public ReleasesController() : this(new ReleaseRepository())
        {
        }

        public ReleasesController(IReleaseRepository releaseRepository)
        {
            if (releaseRepository == null) throw new ArgumentNullException("releaseRepository");
            _releaseRepository = releaseRepository;
        }

        // GET: api/release
        public object Get(HttpRequestMessage message)
        {
            dynamic result = new ExpandoObject();
            result.lastRefresh = DateTime.Now;
            result.urlReleaseExplorer = ConfigurationManager.AppSettings[URL_RELEASE_EXPLORER_KEY];
            result.version = GetApplicationVersion();
            result.releases = new List<dynamic>();

            try
            {
                // determine data-filters based on HTTP headers
                string includedReleasePathIds = null;
                int releaseCount = 5;
                bool showComponents = true;
                if (message.Headers.Contains("includedReleasePathIds"))
                {
                    includedReleasePathIds = message.Headers.GetValues("includedReleasePathIds").First();
                }
                if (message.Headers.Contains("releaseCount"))
                {
                    releaseCount = Convert.ToInt32(message.Headers.GetValues("releaseCount").First());
                }
                if (message.Headers.Contains("showComponents"))
                {
                    showComponents = Convert.ToBoolean(message.Headers.GetValues("showComponents").First());
                }

                if (!IncludedReleasePathIdsValidator.IsValidIncludedReleasePathIds(includedReleasePathIds))
                {
                    return Request.CreateErrorResponse(HttpStatusCode.BadRequest, "Invalid argument includedReleasePathIds");
                }

                // retreive the data
                var data = _releaseRepository.GetReleaseData(includedReleasePathIds, releaseCount);
                var releases = data.Releases;

                // build response datastructure (will be serialized to JSON automatically)
                foreach (var releaseData in releases)
                {
                    // release
                    dynamic release = CreateRelease(releaseData);
                    result.releases.Add(release);
                    
                    // stages
                    var stages = data.Stages
                        .Where(stage => data.StageWorkflows.Any(wf => wf.StageId == stage.Id && wf.ReleaseId == releaseData.Id))
                        .OrderBy(stage => stage.Rank);
                    foreach (var stageData in stages)
                    {
                        dynamic stage = CreateStage(stageData, data.Environments);
                        release.stages.Add(stage);

                        // Steps
                        stage.steps = new List<dynamic>();
                        var steps = data.ReleaseSteps
                            .Where(step => step.StageId == stageData.Id && step.ReleaseId == releaseData.Id)
                            .OrderBy(step => step.Attempt)
                            .ThenBy(step => step.StepRank);
                        foreach (var stepData in steps)
                        {
                            dynamic step = CreateStep(stepData);
                            stage.steps.Add(step);
                        }
                    }

                    // components
                    if (showComponents)
                    {                        
                        var components = data.ReleaseComponents
                            .Where(c => c.ReleaseId == releaseData.Id)
                            .OrderBy(c => c.BuildDefinition);

                        foreach (var componentData in components)
                        {
                            dynamic component = new ExpandoObject();
                            release.components.Add(component);
                            component.build = componentData.Build;
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private static dynamic CreateRelease(Release releaseData)
        {
            dynamic release = new ExpandoObject();
            release.name = releaseData.Name;
            release.status = releaseData.Status;
            release.createdOn = releaseData.CreatedOn;
            release.targetStageId = releaseData.TargetStageId;
            release.releasePathName = releaseData.ReleasePathName;
            release.stages = new List<dynamic>();
            release.components = new List<dynamic>();

            return release;
        }

        private static dynamic CreateStage(Stage stageData, List<Models.Environment> environments)
        {
            dynamic stage = new ExpandoObject();
            stage.id = stageData.Id;
            stage.name = stageData.Name;
            stage.rank = stageData.Rank;

            var environmentData = environments.First(env => env.Id == stageData.EnvironmentId);
            stage.environment = environmentData.Name;

            return stage;
        }

        private static dynamic CreateStep(Step stepData)
        {
            dynamic step = new ExpandoObject();
            step.id = stepData.Id;
            step.name = stepData.Name;
            step.status = stepData.Status;
            step.rank = stepData.StepRank;
            step.createdOn = stepData.CreatedOn;

            return step;
        }
        private static dynamic GetApplicationVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            return fileVersionInfo.FileVersion;
        }

    }
}
