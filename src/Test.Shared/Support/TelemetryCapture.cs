namespace Test.Shared.Support
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using EasySlack;

    /// <summary>
    /// In-memory listener for the EasySlack meter and activity source. Captures every measurement and every completed
    /// span while it is alive. Listeners are process-wide, so assertions only check that expected telemetry is present;
    /// they never assume the capture saw nothing else (other tests may run concurrently).
    /// </summary>
    internal sealed class TelemetryCapture : IDisposable
    {
        /// <summary>
        /// Gets every captured measurement.
        /// </summary>
        public IReadOnlyList<CapturedMeasurement> Measurements
        {
            get { return _Measurements.ToArray(); }
        }

        /// <summary>
        /// Gets every completed span.
        /// </summary>
        public IReadOnlyList<Activity> Spans
        {
            get { return _Spans.ToArray(); }
        }

        private readonly ConcurrentQueue<CapturedMeasurement> _Measurements = new ConcurrentQueue<CapturedMeasurement>();
        private readonly ConcurrentQueue<Activity> _Spans = new ConcurrentQueue<Activity>();
        private readonly MeterListener _MeterListener;
        private readonly ActivityListener _ActivityListener;

        /// <summary>
        /// Initializes a new instance of the <see cref="TelemetryCapture"/> class and starts listening.
        /// </summary>
        public TelemetryCapture()
        {
            _ActivityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == EasySlackTelemetryNames.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => _Spans.Enqueue(activity)
            };
            ActivitySource.AddActivityListener(_ActivityListener);

            _MeterListener = new MeterListener();
            _MeterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == EasySlackTelemetryNames.MeterName) listener.EnableMeasurementEvents(instrument);
            };
            _MeterListener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => Add(instrument, value, tags));
            _MeterListener.SetMeasurementEventCallback<int>((instrument, value, tags, state) => Add(instrument, value, tags));
            _MeterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => Add(instrument, value, tags));
            _MeterListener.Start();
        }

        /// <summary>
        /// Polls every observable instrument (gauges) so their current values are captured.
        /// </summary>
        public void RecordObservables()
        {
            _MeterListener.RecordObservableInstruments();
        }

        /// <summary>
        /// Returns the measurements for an instrument whose tags match.
        /// </summary>
        /// <param name="instrument">The instrument name.</param>
        /// <param name="tags">The expected tags, or null for any.</param>
        /// <returns>The matching measurements.</returns>
        public List<CapturedMeasurement> Find(string instrument, IReadOnlyDictionary<string, string>? tags = null)
        {
            return _Measurements
                .Where(m => m.Instrument == instrument && (tags == null || m.Matches(tags)))
                .ToList();
        }

        /// <summary>
        /// Asserts that at least one measurement for an instrument has the expected tags, and returns the matches.
        /// </summary>
        /// <param name="instrument">The instrument name.</param>
        /// <param name="tags">The expected tags, or null for any.</param>
        /// <returns>The matching measurements.</returns>
        /// <exception cref="TestAssertionException">Thrown when nothing matches.</exception>
        public List<CapturedMeasurement> Require(string instrument, IReadOnlyDictionary<string, string>? tags = null)
        {
            List<CapturedMeasurement> matches = Find(instrument, tags);
            if (matches.Count > 0) return matches;

            string expected = tags == null ? "(any)" : string.Join(", ", tags.Select(t => t.Key + "=" + t.Value));
            string seen = string.Join(" | ", Find(instrument).Select(m => string.Join(", ", m.Tags.Select(t => t.Key + "=" + t.Value))));
            throw new TestAssertionException("Expected a measurement on " + instrument + " with " + expected + ". Seen: " + (seen.Length > 0 ? seen : "(none)"));
        }

        /// <summary>
        /// Returns completed spans with the given name.
        /// </summary>
        /// <param name="name">The span display name.</param>
        /// <returns>The matching spans.</returns>
        public List<Activity> SpansNamed(string name)
        {
            return _Spans.Where(a => a.DisplayName == name).ToList();
        }

        /// <summary>
        /// Returns the single completed span with the given name and tag value.
        /// </summary>
        /// <param name="name">The span display name.</param>
        /// <param name="tagKey">The tag key to match.</param>
        /// <param name="tagValue">The tag value to match.</param>
        /// <returns>The span.</returns>
        /// <exception cref="TestAssertionException">Thrown when no span matches.</exception>
        public Activity RequireSpan(string name, string tagKey, string tagValue)
        {
            Activity? span = _Spans.FirstOrDefault(a => a.DisplayName == name && string.Equals(Convert.ToString(a.GetTagItem(tagKey), System.Globalization.CultureInfo.InvariantCulture), tagValue, StringComparison.Ordinal));
            if (span == null) throw new TestAssertionException("Expected a span named '" + name + "' with " + tagKey + "=" + tagValue + ".");
            return span;
        }

        /// <summary>
        /// Starts a test-owned parent span so spans emitted by the code under test can be isolated by trace id,
        /// even when other tests emit telemetry concurrently. The span is not from the EasySlack source and is not captured.
        /// </summary>
        /// <returns>The started parent span. Dispose it to stop it.</returns>
        public Activity StartTestTrace()
        {
            Activity activity = new Activity("easyslack-telemetry-test");
            activity.SetIdFormat(ActivityIdFormat.W3C);
            return activity.Start();
        }

        /// <summary>
        /// Returns completed spans that belong to the given trace.
        /// </summary>
        /// <param name="traceId">The trace id.</param>
        /// <returns>The spans in that trace.</returns>
        public List<Activity> SpansInTrace(ActivityTraceId traceId)
        {
            return _Spans.Where(a => a.TraceId == traceId).ToList();
        }

        /// <summary>
        /// Waits until a condition over the captured telemetry holds.
        /// </summary>
        /// <param name="condition">The condition.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the condition holds.</returns>
        /// <exception cref="TestAssertionException">Thrown when the condition does not hold within about two seconds.</exception>
        public async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
        {
            for (int i = 0; i < 200; i++)
            {
                if (condition()) return;
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            throw new TestAssertionException("Timed out waiting for the expected telemetry.");
        }

        /// <summary>
        /// Stops listening.
        /// </summary>
        public void Dispose()
        {
            _MeterListener.Dispose();
            _ActivityListener.Dispose();
        }

        private void Add<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags) where T : struct
        {
            double numeric = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            _Measurements.Enqueue(new CapturedMeasurement(instrument.Name, instrument.Unit, numeric, tags));
        }
    }
}
