﻿using Sharpliner.AzureDevOps;
using Sharpliner.AzureDevOps.Tasks;

namespace Sharpliner.Tests.AzureDevOps
{
    /// <summary>
    /// https://github.com/dotnet/xharness/blob/main/azure-pipelines.yml
    /// </summary>
    internal class XHarnessPipeline : AzureDevOpsPipelineDefinition
    {
        public override string TargetFile => "azure-pipelines.yml";

        public override TargetPathType TargetPathType => TargetPathType.RelativeToGitRoot;

        public override AzureDevOpsPipeline Pipeline => new()
        {
            Variables =
            {
                Template("eng/common-variables.yml"),
                Variable("Build.Repository.Clean", true),
            },

            Trigger = new DetailedTrigger
            {
                Batch = true,
                Branches = new()
                {
                    Include = { "main", "xcode/*" }
                }
            },

            Pr = new DetailedPrTrigger()
            {
                Branches = new()
                {
                    Include = { "main", "xcode/*" }
                }
            },

            Stages =
            {
                new Stage("Build_Windows_NT", "Build Windows")
                {
                    Jobs =
                    {
                        new Template<Job>("/eng/common/templates/jobs/jobs.yml")
                        {
                            Parameters =
                            {
                                { "enableTelemetry", true },
                                { "enablePublishBuildArtifacts", true },
                                { "enableMicrobuild", true },
                                { "enablePublishUsingPipelines", true },
                                { "enablePublishBuildAssets", true },
                                { "helixRepo", "dotnet/xharness" },
                                { "jobs", new[]
                                    {
                                        new Job("Windows_NT", "Build Windows_NT")
                                        {
                                            Pool = new HostedPool("windows-2019"),
                                            /* Strategy = ... TODO ..., */
                                            Steps =
                                            {
                                                If_<Step>().Equal(variables["_RunAsPublic"], "False")
                                                    .Step(new CommandLineTask("Build",
                                                            "eng\\common\\CIBuild.cmd" +
                                                            " -configuration $(_BuildConfig)" +
                                                            " -prepareMachine" +
                                                            " $(_InternalBuildArgs)" +
                                                            " /p:Test=false")
                                                        .WhenSucceeded()),

                                                If_<Step>().Equal(variables["_RunAsPublic"], "True")
                                                    .Step(new CommandLineTask("Build and run tests",
                                                            "eng\\common\\CIBuild.cmd" +
                                                            " -configuration $(_BuildConfig)" +
                                                            " -prepareMachine" +
                                                            " $(_InternalBuildArgs)")
                                                        .WhenSucceeded())

                                                    .Step(new AzureDevOpsTask("PublishTestResults@2", "Publish Unit Test Results")
                                                    {
                                                        Inputs =
                                                        {
                                                            { "testResultsFormat", "xUnit" },
                                                            { "testResultsFiles", "$(Build.SourcesDirectory)/artifacts/TestResults/**/*.xml" },
                                                            { "mergeTestResults", true },
                                                            { "searchFolder", "$(system.defaultworkingdirectory)" },
                                                            { "testRunTitle", "XHarness unit tests - $(Agent.JobName)" },
                                                        }
                                                    }.WhenSucceededOrFailed())

                                                    .Step(new AzureDevOpsTask("ComponentGovernanceComponentDetection@0", "Component Governance scan")
                                                    {
                                                        Inputs =
                                                        {
                                                            { "ignoreDirectories", "$(Build.SourcesDirectory)/.packages,$(Build.SourcesDirectory)/artifacts/obj/Microsoft.DotNet.XHarness.CLI/$(_BuildConfig)/net6.0/android-tools-unzipped" },
                                                        }
                                                    }),
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                },

                If_<Stage>().Equal(variables["_RunAsPublic"], "True")
                    .Stage(new Stage("Build_OSX", "Build OSX")
                    {
                        Jobs = {
                            new Template<Job>("/eng/common/templates/jobs/jobs.yml")
                            {
                                Parameters =
                                {
                                    { "enableTelemetry", true },
                                    { "enablePublishBuildArtifacts", true },
                                    { "enableMicrobuild", true },
                                    { "enablePublishUsingPipelines", true },
                                    { "enablePublishBuildAssets", true },
                                    { "helixRepo", "dotnet/xharness" },
                                    { "jobs", new[]
                                        {
                                            new Job("OSX", "Build OSX")
                                            {
                                                Pool = new Pool("Hosted macOS"),
                                                /* Strategy = ... TODO ..., */
                                                Steps =
                                                {
                                                    If_<Step>().Equal(variables["_RunAsPublic"], "False")
                                                        .Step(new CommandLineTask("Build",
                                                                "eng/common/cibuild.sh" +
                                                                " --configuration $(_BuildConfig)" +
                                                                " --prepareMachine" +
                                                                " $(_InternalBuildArgs)" +
                                                                " /p:Test=false")
                                                            .WhenSucceeded()),

                                                    If_<Step>().Equal(variables["_RunAsPublic"], "True")
                                                        .Step(new CommandLineTask("Build and run tests",
                                                                "eng/common/cibuild.sh" +
                                                                " --configuration $(_BuildConfig)" +
                                                                " --prepareMachine" +
                                                                " $(_InternalBuildArgs)")
                                                            .WhenSucceeded())

                                                        .Step(new PublishPipelineArtifactTask(
                                                                "Publish XHarness CLI for Helix Testing",
                                                                "$(Build.SourcesDirectory)/artifacts/packages/$(_BuildConfig)/Shipping/Microsoft.DotNet.XHarness.CLI.1.0.0-ci.nupkg",
                                                                "Microsoft.DotNet.XHarness.CLI.$(_BuildConfig)")
                                                            .When("and(succeeded(), eq(variables['_BuildConfig'], 'Debug'))"))

                                                        .Step(new AzureDevOpsTask("PublishTestResults@2", "Publish Unit Test Results")
                                                        {
                                                            Inputs =
                                                            {
                                                                { "testResultsFormat", "xUnit" },
                                                                { "testResultsFiles", "$(Build.SourcesDirectory)/artifacts/TestResults/**/*.xml" },
                                                                { "mergeTestResults", true },
                                                                { "searchFolder", "$(system.defaultworkingdirectory)" },
                                                                { "testRunTitle", "XHarness unit tests - $(Agent.JobName)" },
                                                            }
                                                        }.WhenSucceededOrFailed())
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    })

                    .Template("eng/e2e-test.yml", new TemplateParameters
                    {
                        { "name", "E2E_Android" },
                        { "displayName", "Android - Simulators" },
                        { "testProject", "$(Build.SourcesDirectory)/tests/integration-tests/Android/Android.Helix.SDK.Tests.proj" },
                    })

                    .Template("eng/e2e-test.yml", new TemplateParameters
                    {
                        { "name", "E2E_Android_Manual_Commands" },
                        { "displayName", "Android - Manual Commands" },
                        { "testProject", "$(Build.SourcesDirectory)/tests/integration-tests/Android/Android.CLI.Commands.Tests.proj" },
                    })

                    .Template("eng/e2e-test.yml", new TemplateParameters
                    {
                        { "name", "E2E_Apple_Simulators" },
                        { "displayName", "Apple - Simulators" },
                        { "testProject", "$(Build.SourcesDirectory)/tests/integration-tests/Apple/Simulator.Tests.proj" },
                    })

                    .Template("eng/e2e-test.yml", new TemplateParameters
                    {
                        { "name", "E2E_SimulatorInstaller" },
                        { "displayName", "Apple - Simulator Commands" },
                        { "testProject", "$(Build.SourcesDirectory)/tests/integration-tests/Apple/SimulatorInstaller.Tests.proj" },
                    })

                    .Template("eng/e2e-test.yml", new TemplateParameters
                    {
                        { "name", "E2E_WASM" },
                        { "displayName", "WASM" },
                        { "testProject", "$(Build.SourcesDirectory)/tests/integration-tests/WASM/WASM.Helix.SDK.Tests.proj" },
                    }),

                If_<Stage>().Equal(variables["_RunAsInternal"], "True")
                    .Template("eng/common/templates/post-build/post-build.yml", new TemplateParameters()
                    {
                        { "publishingInfraVersion", 3 },
                        { "enableSymbolValidation", true },
                        { "enableSourceLinkValidation", false },
                        { "validateDependsOn", new[] { "Build_Windows_NT" } },
                        { "publishDependsOn", new[] { "Validate" } },
                        { "SDLValidationParameters", new TemplateParameters
                            {
                                { "enable", false },
                                { "continueOnError", false },
                                { "params", "-SourceToolsList @(\"policheck\",\"credscan\") " +
                                            "-TsaInstanceURL $(_TsaInstanceURL) " +
                                            "-TsaProjectName $(_TsaProjectName) " +
                                            "-TsaNotificationEmail $(_TsaNotificationEmail) " +
                                            "-TsaCodebaseAdmin $(_TsaCodebaseAdmin) " +
                                            "-TsaBugAreaPath $(_TsaBugAreaPath) " +
                                            "-TsaIterationPath $(_TsaIterationPath) " +
                                            "-TsaRepositoryName \"Arcade\" " +
                                            "-TsaCodebaseName \"Arcade\" " +
                                            "-TsaPublish $True" },
                            }
                        }
                    }),
            }
        };
    }
}