using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.Control;

namespace NOVORA.Network;

/// <summary>PC Internet HTTP request/response timing; never reports game ICMP RTT or tunnel RTT.</summary>
public sealed class NLExperimentalInternetProbe : IDisposable
{
    public const string ProbeUrl="https://www.google.com/generate_204";
    private readonly object _sync=new();
    private readonly TimeProvider _clock;
    private readonly long _generation;
    private readonly string _key;
    private readonly Queue<NLNetworkSample> _samples=new();
    private readonly List<double> _quiet=new();
    private readonly CancellationTokenSource _stop=new();
    private readonly HttpClient _http;
    private long? _calibrationStart;
    private long? _lastSample;
    private bool _confirmed, _started, _disposed;
    public NLExperimentalInternetProbe(long generation,string key,TimeProvider? clock=null,HttpMessageHandler? handler=null)
    {
        _generation=generation; _key=key; _clock=clock??TimeProvider.System;
        _http=handler is null ? new(new HttpClientHandler {AllowAutoRedirect=false}) : new(handler);
        _http.Timeout=TimeSpan.FromSeconds(2);
    }
    public IReadOnlyList<NLNetworkSample> Samples {get {lock(_sync)return _samples.ToArray();}}
    public void BeginCalibration(bool quietConfirmed)
    {
        lock(_sync) { _calibrationStart=_clock.GetTimestamp(); _confirmed=quietConfirmed; _quiet.Clear(); _samples.Clear(); }
    }
    public void RecordResult(double milliseconds,bool success)
    {
        lock(_sync)
        {
            if(_disposed)return;
            long now=_clock.GetTimestamp();
            bool valid=success && double.IsFinite(milliseconds) && milliseconds>=0 && milliseconds<=2000;
            foreach(var direction in Enum.GetValues<NLNetworkDirection>())
            {
                if(_samples.Count>=64)_samples.Dequeue();
                _samples.Enqueue(new(_generation,_key,NLNetworkDomain.Internet,direction,NLNetworkSampleSource.InternetPcHttp,
                    now,_lastSample.HasValue ? Math.Max(.001,_clock.GetElapsedTime(_lastSample.Value,now).TotalSeconds) : .5,
                    valid ? milliseconds : null,null,null,0,0,0,0,valid));
            }
            _lastSample=now;
            while(_samples.Count>0 && _clock.GetElapsedTime(_samples.Peek().ObservedAtTicks,now).TotalSeconds>10)_samples.Dequeue();
            if(_confirmed && _calibrationStart is { } start && _clock.GetElapsedTime(start,now).TotalSeconds<=30)
            { if(!valid)_confirmed=false; else if(_quiet.Count<128)_quiet.Add(milliseconds); }
        }
    }
    public NLNetworkBaseline? TryGetBaseline()
    {
        lock(_sync)
        {
            if(!_confirmed || _calibrationStart is not { } start || _clock.GetElapsedTime(start).TotalSeconds<30 || _quiet.Count<30)return null;
            var sorted=_quiet.Order().ToArray(); double p95=sorted[(int)Math.Ceiling(sorted.Length*.95)-1];
            return new(_generation,_key,NLNetworkDomain.Internet,NLNetworkSampleSource.InternetPcHttp,p95,_clock.GetTimestamp(),true);
        }
    }
    public void Start()
    {
        lock(_sync) { ObjectDisposedException.ThrowIf(_disposed,this); if(_started)return; _started=true; }
        _=RunAsync();
    }
    private async Task RunAsync()
    {
        try
        {
            while(!_stop.IsCancellationRequested)
            {
                long begin=_clock.GetTimestamp(); bool success=false;
                try
                {
                    using var response=await _http.GetAsync(ProbeUrl,HttpCompletionOption.ResponseHeadersRead,_stop.Token).ConfigureAwait(false);
                    success=response.StatusCode==HttpStatusCode.NoContent;
                }
                catch(HttpRequestException) { }
                catch(OperationCanceledException) when(!_stop.IsCancellationRequested) { }
                RecordResult(_clock.GetElapsedTime(begin).TotalMilliseconds,success);
                await Task.Delay(TimeSpan.FromMilliseconds(500),_clock,_stop.Token).ConfigureAwait(false);
            }
        }
        catch(OperationCanceledException) { }
        catch(ObjectDisposedException) { }
    }
    public void Dispose()
    {
        lock(_sync) { if(_disposed)return; _disposed=true; _stop.Cancel(); _http.Dispose(); _samples.Clear(); }
    }
}
