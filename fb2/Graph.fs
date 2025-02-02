namespace IncrementalBuild
open System.IO
open Model

module Application =

    type DotnetApplicationProperties = {
        DependsOn : string array
        Publish : Project -> unit
        Deploy : Artifact[] -> unit
    }

    type CustomApplicationProperties = {
        DependsOn : string array
        Publish : unit -> unit
        Deploy : Artifact[] -> unit
    }

    type BuildDirectives = {
        AlwaysRebuild : bool
        IgnoreBuild : bool
    }

    let defaultDirectives = {AlwaysRebuild = false; IgnoreBuild = false}

    let dotnetSpecificBuild name buildDirectives (parameters:DotnetApplicationProperties) =
        {
            Name = name
            DependsOn = parameters.DependsOn
            Parameters = DotnetApplication {DotnetApplication.Publish = parameters.Publish }
            Deploy = parameters.Deploy
            AlwaysRebuild = buildDirectives.AlwaysRebuild
            IgnoreBuild = buildDirectives.IgnoreBuild
        }

    let dotnet name (parameters:DotnetApplicationProperties) =
        parameters |> (dotnetSpecificBuild name defaultDirectives)

    let customSpecificBuild name buildDirectives (parameters:CustomApplicationProperties) =
        if parameters.DependsOn |> Array.isEmpty then failwithf "Custom application should have at least one folder dependency for app %s" name
        {
            Name = name
            DependsOn = parameters.DependsOn
            Parameters = CustomApplication { Publish = parameters.Publish }
            Deploy = parameters.Deploy
            AlwaysRebuild = buildDirectives.AlwaysRebuild
            IgnoreBuild = buildDirectives.IgnoreBuild
        }

    let rec custom name (parameters:CustomApplicationProperties) =
        parameters |> (customSpecificBuild name defaultDirectives)


module Graph =
    let readProjectStructure dir (apps:Application array) (projects: Project seq) =
        let projects =
            projects
            |> Seq.map (fun p -> p.ProjectPath, p)
            |> Map.ofSeq

        let invalidProjects =
            projects
            |> Map.filter (fun _ project ->
                                project.ProjectReferences
                                |> Array.exists (fun dep -> projects |> Map.containsKey dep |> not)
                           )

        invalidProjects
            |> Map.iter ( fun projectFile project -> printfn "WARNING: broken dependencies in %s project file. Ignoring project" projectFile)

        projects
        |> Map.filter (fun _ project ->
            apps
                |> Array.exists (fun app ->
                                  match app.Parameters with
                                  | DotnetApplication _ -> app.Name = project.Name
                                  | _ -> false )
                |> not
        )
        |> Map.iter (fun projectFile project -> if project.IsPublishable then printfn "WARNING: not publishable project %s will be published. Add <IsPublishable>false</IsPublishable> property" projectFile else ())

        let validProjects =
            projects
            |> Map.filter (fun projectFile _ -> invalidProjects |> Map.containsKey projectFile |> not)
            |> Map.toArray
            |> Array.map snd

        apps |> Array.iter (fun app ->
            match app.Parameters with
            | DotnetApplication _ ->
                if validProjects |> Array.exists(fun project -> project.Name = app.Name) |> not then
                    failwithf "Application %s not found in project structure" app.Name
            | CustomApplication _ ->
                for dependsDir in app.DependsOn do
                    if dependsDir |> Pathes.combine dir |> Directory.Exists |> not then
                        failwithf "Application %s not found in the repository folder" app.Name
        )

        {
            Applications = apps
            Projects = validProjects
            RootFolder = dir |> Path.GetFullPath
        }

    let rec getReferencedProjects (projectMap:Map<string, Project>) project = seq {
        yield! project.ProjectReferences |> Seq.map (fun p -> projectMap.[p])
        yield! project.ProjectReferences |> Seq.collect (fun p -> projectMap.[p] |> getReferencedProjects projectMap)
    }
    let rec getDependentProjects structure (project: Project) = seq {
        let dependentProjects =
            structure.Projects
            |> Seq.filter (fun p -> p.ProjectReferences
                                    |> Array.contains project.ProjectPath)
        yield! dependentProjects
        yield! dependentProjects |> Seq.collect (getDependentProjects structure)
    }
    let rec getProjectWithReferencedProjects structure project =
        let projectMap = structure.Projects |> Array.map (fun p -> p.ProjectPath, p) |> Map.ofSeq
        seq {
            yield project
            yield! getReferencedProjects projectMap project
        }
    let rec getProjectWithDependentProjects structure project =
        seq {
            yield project
            yield! getDependentProjects structure project
        }

    let getImpactedProjects structure (files:string array) =

        let getCorrespondingApplication (project:Project) =
            structure.Applications |> Array.tryFind (fun app -> app.Name = project.Name)

        let getCorrespondingDotnetProject (app:Application) =
            match app.Parameters with
            | DotnetApplication _ ->
                structure.Projects
                    |> Array.find (fun p -> p.Name = app.Name)
                    |> Some
            | _ ->
                None

        let isProjectImpacted project =
            files
            |> Seq.exists(fun f -> project.ProjectFolder |> f.StartsWith ||
                                   project.ExternalReferences
                                   |> Seq.exists(fun extRef -> extRef |> f.StartsWith))

        let directImpactedProjects =
            structure.Projects
            |> Array.where isProjectImpacted

        let directImpactedApplications =
            structure.Applications
                |> Array.where (
                    fun app -> app.DependsOn
                                |> Array.map Pathes.ensureDirSeparator
                                |> Array.exists(fun dependsOnDir -> files |> Seq.exists (fun f -> dependsOnDir |> f.StartsWith))
                )

        let getAllAlwaysRebuildApplications =
            structure.Applications
            |> Array.where (fun app ->
                printfn "application %s has a always-rebuild option = %b" app.Name app.AlwaysRebuild
                app.AlwaysRebuild = true)

        let getAllIgnoredApplication =
            structure.Applications
            |> Array.where(fun app ->
                printfn "application %s has an ignore option = %b" app.Name app.IgnoreBuild
                app.IgnoreBuild = true)

        let allImpactedProjects =
            seq {
                yield! directImpactedProjects |> Seq.collect (fun p -> p |> getProjectWithDependentProjects structure)
                yield! directImpactedApplications |> Array.choose getCorrespondingDotnetProject
                yield! getAllAlwaysRebuildApplications |> Array.choose getCorrespondingDotnetProject
            }
            |> Seq.distinct
            |> Seq.filter (fun project ->
                getAllIgnoredApplication |>
                Array.choose getCorrespondingDotnetProject |>
                Seq.contains project |> not)
            |> Array.ofSeq

        let allImpactedApplications =
            seq {
                yield! directImpactedApplications
                yield! getAllAlwaysRebuildApplications
                yield! allImpactedProjects |> Array.choose getCorrespondingApplication
            }
            |> Seq.distinctBy(fun app -> app.Name)
            |> Seq.filter (fun app ->
                getAllIgnoredApplication |>
                Seq.where(fun a -> a.Name = app.Name) |> Seq.isEmpty)
            |> Array.ofSeq
        {
            Applications = allImpactedApplications
            Projects = allImpactedProjects
            RootFolder = structure.RootFolder
        }
