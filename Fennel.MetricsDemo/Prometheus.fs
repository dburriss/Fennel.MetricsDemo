namespace Fennel.MetricsDemo

open System
open System.Collections.Generic
open System.Text
open Azure.Storage.Queues.Models
open Microsoft.AspNetCore.Mvc
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Extensions.Http
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.Http
open Fennel
open Azure.Storage.Queues

type PrometheusLogBuilder() =
    let definitions = Dictionary<MetricName,(MetricType * DocString option)> ()
    member this.Define(name, metricType, ?helpText) =
        let k = MetricName name
        let help = Option.map DocString helpText
        if definitions.ContainsKey k then
            definitions.[k] = (metricType,help) |> ignore
        else definitions.Add(k, (metricType,help))
        this
        
    member this.Stringify (metrics : Line array) =
        let set = Set.empty<MetricName>
        let insertInfo = function
            | Help _ | Type _ | Comment _ | Blank -> Array.empty
            | Metric m ->
                if Set.contains m.Name set then [||]
                else
                    if definitions.ContainsKey m.Name then
                        let (t, doc) = definitions.[m.Name]
                        let help = doc |> Option.map (fun d -> Line.Help (m.Name, d))
                        let metricType = Line.Type (m.Name, t)
                        [| help ; Some metricType |] |> Array.choose id
                    else failwithf "%A not defined for this builder." m
            
        metrics
        |> Array.map (fun m -> Array.append (insertInfo m) [|m|])
        |> Array.concat
        |> Array.map Line.asString
        |> String.concat "\n"
        |> fun s -> s.TrimEnd()
        
        

//type CounterFactory(name, help) =
//    member this.Inc() = ()
//
//module MetricFactory =
//    let counter name help = CounterFactory(name, help)
    
module Prometheus =
    
    let mutable types = Some [
        Prometheus.help "sale_count" "Number of sales that have occurred."
        Prometheus.typeHint "sale_count" MetricType.Counter
    ]
    let metricsBuilder = PrometheusLogBuilder()
                            .Define("sale_count", MetricType.Counter, "Number of sales that have occurred.")
    let queueMetrics (queue : ICollector<string>) ms = queue.Add(ms |> String.concat "\n")
        
    [<FunctionName("MetricsGenerator")>]
    let metricsGenerator([<TimerTrigger("*/6 * * * * *")>]myTimer: TimerInfo, [<Queue("logs")>] queue : ICollector<string>, log: ILogger) =
        let msg = sprintf "Generating sales at: %A" DateTime.Now
        log.LogInformation msg
        let qMetrics = queueMetrics queue
//        match types with
//        | None -> ()
//        | Some ts -> 
//            qMetrics ts
//            //types <- None
        let rnd = Random()
        let sales = rnd.Next(0, 50) |> float
        
//        qMetrics [ Prometheus.metricSimple "sale_count" sales ]
        queue.Add( metricsBuilder.Stringify [|
            Line.metric (MetricName "sale_count") (MetricValue.FloatValue sales) [] None
        |] )
        log.LogInformation (sprintf "Sales : %f" sales)

    [<FunctionName("metrics")>]
    let metrics ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)>]req: HttpRequest) (log: ILogger) =
        async {
            log.LogInformation("Fetching prometheus metrics...")
            let queueName = "logs"
            let connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage", EnvironmentVariableTarget.Process)
            let queueClient = QueueClient(connectionString, queueName)
            if queueClient.Exists().Value then
                let messages = queueClient.ReceiveMessages(Nullable<int>(32), Nullable<TimeSpan>(TimeSpan.FromSeconds(20.))).Value
                log.LogInformation(sprintf "Received %i logs." messages.Length)
                let sb = StringBuilder()
                let processMessage (msg : QueueMessage) =
                    let txt = Encoding.UTF8.GetString(Convert.FromBase64String(msg.MessageText))
                    sb.Append(sprintf "%s\n" txt) |> ignore
                    queueClient.DeleteMessage(msg.MessageId, msg.PopReceipt) |> ignore
                messages |> Array.iter processMessage
                let responseTxt = sb.ToString().TrimEnd()
                let response = ContentResult()
                response.Content <- responseTxt
                response.ContentType <- "text/plain; version=0.0.4"
                response.StatusCode <- Nullable<int>(200)
                return response :> IActionResult
            else return NoContentResult() :> IActionResult
            
        } |> Async.StartAsTask