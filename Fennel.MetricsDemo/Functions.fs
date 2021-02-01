namespace Fennel.MetricsDemo

open System
open System.Text
open Azure.Storage.Queues.Models
open Microsoft.AspNetCore.Mvc
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Extensions.Http
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.Http
open Fennel
open Azure.Storage.Queues

module Functions =
    
    let metricsBuilder = PrometheusLogBuilder()
                            .Define("demo_sale_count", MetricType.Counter, "Number of sales that have occurred.")
        
    [<FunctionName("MetricsGenerator")>]
    let metricsGenerator([<TimerTrigger("*/6 * * * * *")>]myTimer: TimerInfo, [<Queue("logs")>] queue : ICollector<string>, log: ILogger) =
        let msg = sprintf "Generating sales at: %A" DateTime.Now
        log.LogInformation msg
        let sales = Random().Next(0, 50) |> float
        let metric = Line.metric (MetricName "demo_sale_count") (MetricValue.FloatValue sales) [] (Some(Timestamp DateTimeOffset.UtcNow))

        queue.Add(Line.asString metric)
        log.LogInformation (sprintf "Sales : %f" sales)

    [<FunctionName("metrics")>]
    let metrics ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)>]req: HttpRequest) (log: ILogger) =
        async {
            log.LogInformation("Fetching prometheus metrics...")
            // setup queue client
            let queueName = "logs"
            let connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage", EnvironmentVariableTarget.Process)
            let queueClient = QueueClient(connectionString, queueName)
            
            if queueClient.Exists().Value then
                // receive messages
                let messages = queueClient.ReceiveMessages(Nullable<int>(32), Nullable<TimeSpan>(TimeSpan.FromSeconds(20.))).Value
                log.LogInformation(sprintf "Received %i logs." messages.Length)
                // return message as text
                let processMessage (msg : QueueMessage) =
                    let txt = Encoding.UTF8.GetString(Convert.FromBase64String(msg.MessageText))
                    queueClient.DeleteMessage(msg.MessageId, msg.PopReceipt) |> ignore
                    txt
                let metrics = messages |> Array.map processMessage
                // build up Prometheus text
                let responseTxt = metricsBuilder.Build(metrics)
                
                // return as Prometheus HTTP content
                let response = ContentResult()
                response.Content <- responseTxt
                response.ContentType <- "text/plain; version=0.0.4"
                response.StatusCode <- Nullable<int>(200)
                return response :> IActionResult
            else return NoContentResult() :> IActionResult
            
        } |> Async.StartAsTask