module Tests

open Fennel.MetricsDemo
open Xunit
open Fennel
open Fennel.CSharp

let getResult = function | Ok x -> x | Error err -> failwith err

[<Fact>]
let ``Builder with undefined metric throws`` () =
    let builder = PrometheusLogBuilder()
    let metricTxt = Prometheus.Metric("test", 0.1)
    let invoke = fun () -> builder.Build([| metricTxt |]) |> ignore
    Assert.Throws<exn>(invoke)
    
[<Fact>]
let ``Builder with different metric definition throws`` () =
    let builder = PrometheusLogBuilder().Define("other", MetricType.Counter)
    let metricTxt = Prometheus.Metric("test", 0.1)
    let invoke = fun () -> builder.Build([| metricTxt |]) |> ignore
    Assert.Throws<exn>(invoke)
        
[<Fact>]
let ``Builder with metric definition without help returns TYPE and METRIC`` () =
    let builder = PrometheusLogBuilder().Define("test", MetricType.Counter)
    let metricTxt = Prometheus.Metric("test", 1.0)
    let prometheusTxt = builder.Build([| metricTxt |])
    let metrics = Prometheus.parseText prometheusTxt |> Array.map getResult
    let expected0 = Line.Type (MetricName "test", MetricType.Counter)
    let expected1 = Line.metric (MetricName "test") (MetricValue.FloatValue 1.0) [] None
    Assert.Equal(expected0, metrics.[0])
    Assert.Equal(expected1, metrics.[1])
           
[<Fact>]
let ``Builder with metric definition with help returns TYPE and METRIC`` () =
    let builder = PrometheusLogBuilder().Define("test", MetricType.Counter, "This is help text")
    let metricTxt = Prometheus.Metric("test", 1.0)
    let prometheusTxt = builder.Build([| metricTxt |])
    let metrics = Prometheus.parseText prometheusTxt |> Array.map getResult
    let expected0 = Line.Help (MetricName "test", DocString "This is help text")
    let expected1 = Line.Type (MetricName "test", MetricType.Counter)
    let expected2 = Line.metric (MetricName "test") (MetricValue.FloatValue 1.0) [] None
    Assert.Equal(expected0, metrics.[0])
    Assert.Equal(expected1, metrics.[1])
    Assert.Equal(expected2, metrics.[2])
               
[<Fact>]
let ``Builder with 2 metrics only has single HELP and TYPE`` () =
    let builder = PrometheusLogBuilder()
                      .Define("test1", MetricType.Counter, "This is help text for 1")
    let metricTxt1 = Prometheus.Metric("test1", 1.0)
    let metricTxt2 = Prometheus.Metric("test1", 2.0)
    let prometheusTxt = builder.Build([| metricTxt1; metricTxt2 |])
    let metrics = Prometheus.parseText prometheusTxt |> Array.map getResult
    let filterHelps = function | Line.Help _ -> true | _ -> false 
    let filterTypes = function | Line.Type _ -> true | _ -> false 
    let helps = metrics |> Array.filter filterHelps
    let types = metrics |> Array.filter filterTypes
    Assert.Equal(1, helps.Length)
    Assert.Equal(1, types.Length)
    