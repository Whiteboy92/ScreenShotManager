using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Makaretu.Dns;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services;

namespace ScreenShotManager.Tests.MusicSync;

public class MdnsAdvertiserTests
{
    private const int TestPort = 47999;

    /// <summary>
    /// The phone treats the announcement as "the PC is on". A PC that loses power sends no goodbye,
    /// so every record must expire quickly or phones keep believing in a dead PC for over an hour.
    /// </summary>
    [Fact]
    public async Task Announced_records_expire_within_two_minutes()
    {
        var ttls = new ConcurrentBag<(string Name, DnsType Type, TimeSpan Ttl)>();
        using var listener = new MulticastService();
        listener.AnswerReceived += (_, e) =>
        {
            var records = e.Message.Answers.Concat(e.Message.AdditionalRecords).ToList();
            // Only our test advertiser's answers: an installed copy of the app may be announcing too.
            if (!records.OfType<SRVRecord>().Any(s => s.Port == TestPort))
            {
                return;
            }

            foreach (var r in records)
            {
                if (r.CanonicalName.Contains("_musicsync", StringComparison.OrdinalIgnoreCase)
                    || r.Name.ToString().Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                {
                    ttls.Add((r.Name.ToString(), r.Type, r.TTL));
                }
            }
        };
        listener.Start();

        using var advertiser = new MdnsAdvertiser();
        advertiser.Start(TestPort, "AB" + new string('0', 62));
        listener.SendQuery(MusicSyncConstants.MdnsServiceType + ".local", type: DnsType.PTR);

        for (var i = 0; i < 40 && ttls.IsEmpty; i++)
        {
            await Task.Delay(100);
        }

        Assert.NotEmpty(ttls);
        var tooLong = ttls.Where(t => t.Ttl > MdnsAdvertiser.PresenceTtl && t.Ttl > TimeSpan.Zero).ToList();
        Assert.True(tooLong.Count == 0, string.Join(", ", tooLong.Select(t => $"{t.Type} {t.Name} {t.Ttl}")));
    }
}
