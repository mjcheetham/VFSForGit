using GVFS.Common.Git;
using Microsoft.ApplicationInsights;
using System;
using System.Collections.Generic;

namespace GVFS.Common.Tracing
{
    public class AppInsightsEventListener : InProcEventListener
    {
        private readonly string providerName;
        private readonly string[] traitsList;
        private readonly string instrumentationKey;
        private readonly string enlistmentId;
        private readonly string mountId;

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Microsoft.StyleCop.CSharp.NamingRules",
            "SA1305:FieldNamesMustNotUseHungarianNotation",
            Justification = "The 'ai' prefix is not Hungarian notation; it's short for AppInsights.")]
        private readonly TelemetryClient aiClient;

        private AppInsightsEventListener(string providerName, string[] traitsList, string enlistmentId, string mountId, string instrumentationKey)
            : base(EventLevel.Verbose, Keywords.Telemetry)
        {
            this.providerName = providerName;
            this.traitsList = traitsList;
            this.instrumentationKey = instrumentationKey;
            this.enlistmentId = enlistmentId;
            this.mountId = mountId;

            this.aiClient = new TelemetryClient();
            this.aiClient.Context.Session.Id = this.mountId;
            this.aiClient.Context.User.AuthenticatedUserId = GVFSPlatform.Instance.GetCurrentUser();
            this.aiClient.Context.Device.OperatingSystem = GVFSPlatform.Instance.GetOSVersionInformation();
            this.aiClient.Context.GlobalProperties["enlistmentId"] = this.enlistmentId;
            this.aiClient.Context.GlobalProperties["mountId"] = this.mountId;
            this.aiClient.Context.GlobalProperties["providerName"] = this.providerName;
            this.aiClient.InstrumentationKey = this.instrumentationKey;
        }

        public static AppInsightsEventListener CreateTelemetryListenerIfEnabled(string gitBinRoot, string providerName, string enlistmentId, string mountId)
        {
            // This listener is disabled unless the user specifies the proper git config setting.

            string traits = GetConfigValue(gitBinRoot, GVFSConstants.GitConfig.GVFSTelemetryId);
            if (!string.IsNullOrEmpty(traits))
            {
                string[] traitsList = traits.Split('|');
                string ikey = GetConfigValue(gitBinRoot, GVFSConstants.GitConfig.IKey);
                return new AppInsightsEventListener(providerName, traitsList, enlistmentId, mountId, ikey);
            }
            else
            {
                return null;
            }
        }

        public override void Dispose()
        {
            this.aiClient.Flush();
            base.Dispose();
        }

        protected override void RecordMessageInternal(
            string eventName,
            Guid activityId,
            Guid parentActivityId,
            EventLevel level,
            Keywords keywords,
            EventOpcode opcode,
            string payload)
        {
            int levelInt = (int)level;
            int keywordsInt = (int)keywords;
            int opcodeInt = (int)opcode;

            var properties = new Dictionary<string, string>
            {
                ["activityId"] = activityId.ToString("D"),
                ["parentActivityId"] = parentActivityId.ToString("D"),
                ["level"] = levelInt.ToString(),
                ["keywords"] = keywordsInt.ToString(),
                ["opCode"] = opcodeInt.ToString(),
                ["traits"] = string.Join("|", this.traitsList),
                ["json"] = payload
            };

            this.aiClient.TrackEvent(eventName, properties);
        }

        private static string GetConfigValue(string gitBinRoot, string configKey)
        {
            GitProcess.Result result = GitProcess.GetFromSystemConfig(gitBinRoot, configKey);
            if (result.HasErrors || string.IsNullOrEmpty(result.Output.TrimEnd('\r', '\n')))
            {
                result = GitProcess.GetFromGlobalConfig(gitBinRoot, configKey);
            }

            if (result.HasErrors || string.IsNullOrEmpty(result.Output.TrimEnd('\r', '\n')))
            {
                return string.Empty;
            }

            return result.Output.TrimEnd('\r', '\n');
        }
    }
}
