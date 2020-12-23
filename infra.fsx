#r "nuget: Farmer"

open System
open Farmer
open Farmer.Builders

let rg = fsi.CommandLineArgs |> Array.tryItem 1 |> Option.defaultValue "FsAdvent"

let appName = "FennelMetricsDemo"
let servicePlan = "FsAdvent2020"
let tool = "Farmer"
let date = DateTimeOffset.UtcNow.ToString("o")
let createdBy = "Devon Burriss"
let func = functions {
    name appName
    service_plan_name servicePlan
    app_insights_off
    add_tags [
        ("Tool", tool)
        ("ResourceCreationDate", date)
        ("CreatedBy", createdBy)
    ]
}

let deployment = arm {
    location Location.WestEurope
    add_resource func
}

printfn "Deploying to ResourceGroup: %s" rg
deployment |> Deploy.execute rg Deploy.NoParameters