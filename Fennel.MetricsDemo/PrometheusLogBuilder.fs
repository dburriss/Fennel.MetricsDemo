namespace Fennel.MetricsDemo

open System
open System.Collections.Generic
open Fennel

type PrometheusLogBuilder() =
    let definitions = Dictionary<MetricName,(MetricType * DocString option)> ()
    
    let toMetrics (ss : string array) =[|
        for s in ss do
            match (Prometheus.parseLine s) with
            | Ok x -> yield x
            | Error err -> Diagnostics.Debug.WriteLine(err)
        |]
    
    member this.Define(name, metricType, ?helpText) =
        let k = MetricName name
        let help = Option.map DocString helpText
        if definitions.ContainsKey k then
            definitions.[k] = (metricType,help) |> ignore
        else definitions.Add(k, (metricType,help))
        this
    
    member this.Build (smetrics : string array) =
        let mutable set = Set.empty<MetricName>
        let insertInfo = function
            | Help _ | Type _ | Comment _ | Blank -> Array.empty
            | Metric m ->
                if Set.contains m.Name set then [||]
                else
                    if definitions.ContainsKey m.Name then
                        let (t, doc) = definitions.[m.Name]
                        let help = doc |> Option.map (fun d -> Line.Help (m.Name, d))
                        let metricType = Line.Type (m.Name, t)
                        set <- set.Add m.Name
                        let info = [| help ; Some metricType |] |> Array.choose id
                        info
                    else failwithf "%A not defined for this builder." m
            
        smetrics |> toMetrics
        |> Array.map (fun m -> Array.append (insertInfo m) [|m|])
        |> Array.concat
        |> Array.map Line.asString
        |> String.concat "\n"
        |> fun s -> s.TrimEnd()
        