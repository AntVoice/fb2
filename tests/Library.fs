﻿namespace fb2.UnitTests

open IncrementalBuild
open Xunit
open Model

module Tests =
    open System.IO
    open NFluent

    [<Fact>]
    let ``Should properly read project structure`` () =
        let repoRelativePath = "../../../../"
        let repoAbsolutePath = Path.Combine(Directory.GetCurrentDirectory(), repoRelativePath) |> Path.GetFullPath
        let projects = repoRelativePath |> DotnetProjectParser.parseDotnetProjects
        let structure = Graph.readProjectStructure repoRelativePath [||] projects
        let project name = structure.Projects |> Array.find (fun p -> p.Name = name)

        let folders =
            structure.Projects
            |> Array.map (fun p -> p.ProjectFolder)
            |> Array.sort

        Check.That(structure.RootFolder).IsEqualTo(repoAbsolutePath) |> ignore
        Check.That(seq folders).ContainsExactly("/", "fb2/", "tests/") |> ignore
        Check.That(seq (project "tests").ProjectReferences).ContainsExactly("fb2/fb2.fsproj") |> ignore

    [<Fact>]
    let ``Impacted projects detection`` () =
        let structure = {
            ProjectStructure.RootFolder = "/somefolder"
            Projects = [|
                Project.Create "Project1" "Project1Assembly" "project1/project1.csproj" [||] [||] "standard2.0" true
            |]
            Applications = [||]
        }
        let files = [|"project1/file1.txt"|]
        let impacted = Graph.getImpactedProjects structure files
        Check.That(impacted.Projects).Not.IsEmpty() |> ignore

    [<Fact>]
    let ``Impacted projects - by project dependency`` () =
        let structure = {
            ProjectStructure.RootFolder = "/somefolder"
            Projects = [|
                Project.Create "Project1" "Project1Assembly" "project1/project1.csproj" [||] [||] "standard2.0" true
                Project.Create "Project2" "Project1Assembly" "project2/project2.csproj" [|"project1/project1.csproj"|] [||] "standard2.0" true
            |]
            Applications = [||]
        }
        let files = [|"project1/file1.txt"|]
        let impacted = Graph.getImpactedProjects structure files
        Check.That(seq impacted.Projects).HasSize(int64 2) |> ignore

    [<Fact>]
    let ``Impacted projects - by project external dependency`` () =
        let structure = {
            ProjectStructure.RootFolder = "/somefolder"
            Projects = [|
                Project.Create "Project1" "Project1Assembly" "project1/project1.csproj" [||] [||] "standard2.0" true
                Project.Create "Project2" "Project1Assembly" "project2/project2.csproj" [||] [|"external_deps/schema.proto"|] "standard2.0" true
            |]
            Applications = [||]
        }
        let files = [|"external_deps/schema.proto"|]
        let impacted = Graph.getImpactedProjects structure files
        Check.That(seq impacted.Projects).HasSize(int64 1) |> ignore
        Check.That((impacted.Projects |> Array.head).Name).Equals("Project2") |> ignore

    [<Fact>]
    let ``Impacted applications detection - project trigger`` () =
        let structure = {
            ProjectStructure.RootFolder = "/somefolder"
            Projects = [|
                Project.Create "Project1" "Project1Assembly" "project1/project1.csproj" [||] [||] "standard2.0" true
            |]
            Applications = [|
                {
                    Application.Name = "Project1"
                    DependsOn = [||]
                    Parameters = { DotnetApplication.Publish = ignore } |> DotnetApplication
                    Deploy = ignore
                    AlwaysRebuild = false
                    IgnoreBuild = false
                }
            |]
        }
        let files = [|"project1/file1.txt"|]
        let impacted = Graph.getImpactedProjects structure files
        Check.That(impacted.Projects).Not.IsEmpty() |> ignore
        Check.That(impacted.Applications).Not.IsEmpty() |> ignore

    [<Fact>]
    let ``Impacted applications detection - depends on trigger`` () =
        let structure = {
            ProjectStructure.RootFolder = "/somefolder"
            Projects = [|
                Project.Create "Project1" "Project1Assembly" "project1/project1.csproj" [||] [||] "standard2.0" true
            |]
            Applications = [|
                {
                    Application.Name = "Project1"
                    DependsOn = [|"somefolder"|]
                    Parameters = { DotnetApplication.Publish = ignore } |> DotnetApplication
                    Deploy = ignore
                    AlwaysRebuild = false
                    IgnoreBuild = false
                }
            |]
        }
        let files = [|"somefolder/file1.txt"|]
        let impacted = Graph.getImpactedProjects structure files
        Check.That(impacted.Projects).Not.IsEmpty() |> ignore
        Check.That(impacted.Applications).Not.IsEmpty() |> ignore

    [<Fact>]
    let ``Impacted applications detection - custom app - base folder trigger`` () =
        let structure = {
            ProjectStructure.RootFolder = "/somefolder"
            Projects = [||]
            Applications = [|
                {
                    Application.Name = "Project1"
                    DependsOn = [|"somefolder"|]
                    Parameters = { CustomApplication.Publish = ignore } |> CustomApplication
                    Deploy = ignore
                    AlwaysRebuild = false
                    IgnoreBuild = false
                }
            |]
        }
        let files = [|"somefolder/file1.txt"|]
        let impacted = Graph.getImpactedProjects structure files
        Check.That(impacted.Projects).IsEmpty() |> ignore
        Check.That(impacted.Applications).Not.IsEmpty() |> ignore
    
    [<Fact>]
    let ``Ignored applications detection - custom app - base folder trigger`` () =
        let structure = {
            ProjectStructure.RootFolder = "/somefolder"
            Projects = [||]
            Applications = [|
                {
                    Application.Name = "Project1"
                    DependsOn = [|"somefolder"|]
                    Parameters = { CustomApplication.Publish = ignore } |> CustomApplication
                    Deploy = ignore
                    AlwaysRebuild = true
                    IgnoreBuild = true
                }
            |]
        }
        let files = [|"somefolder/file1.txt"|]
        let impacted = Graph.getImpactedProjects structure files
        Check.That(impacted.Projects).IsEmpty() |> ignore
        Check.That(impacted.Applications).IsEmpty() |> ignore
