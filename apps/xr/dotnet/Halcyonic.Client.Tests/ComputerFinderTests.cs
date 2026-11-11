using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// Finding a computer whose pairing is open (ADR 0017, amendment of 2026-11-11): one legacy unicast mDNS
/// query, the answers read as offers only. Here the answers are built as macOS's mDNSResponder sends them.
/// </summary>
public class ComputerFinderTests
{
    private const string Pin = "0123456789abcdef";

    /// <summary>A DNS message: header, then each record as owner, type, class, TTL and data.</summary>
    private sealed class Message
    {
        private readonly List<byte> _bytes = new() { 0, 0, 0x84, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private int _answers;

        public Message Question(string name)
        {
            _bytes[5] = 1;
            _bytes.AddRange(Name(name));
            _bytes.AddRange(new byte[] { 0, 12, 0, 1 });
            return this;
        }

        public int Offset => _bytes.Count;

        public Message Record(IEnumerable<byte> owner, int type, IReadOnlyList<byte> data)
        {
            _bytes.AddRange(owner);
            _bytes.AddRange(new[] { (byte)(type >> 8), (byte)type, (byte)0x80, (byte)1, (byte)0, (byte)0, (byte)0x11, (byte)0x94 });
            _bytes.Add((byte)(data.Count >> 8));
            _bytes.Add((byte)data.Count);
            _bytes.AddRange(data);
            _answers++;
            _bytes[7] = (byte)_answers;
            return this;
        }

        public byte[] Bytes() => _bytes.ToArray();
    }

    private static byte[] Name(string name) =>
        name.Split('.').SelectMany(label => new[] { (byte)Encoding.UTF8.GetByteCount(label) }.Concat(Encoding.UTF8.GetBytes(label)))
            .Append((byte)0).ToArray();

    private static byte[] Pointer(int offset) => new[] { (byte)(0xC0 | (offset >> 8)), (byte)offset };

    private static byte[] Srv(int port, IEnumerable<byte> target) =>
        new byte[] { 0, 0, 0, 0, (byte)(port >> 8), (byte)port }.Concat(target).ToArray();

    private static byte[] Txt(params string[] strings) =>
        strings.SelectMany(text => new[] { (byte)Encoding.UTF8.GetByteCount(text) }.Concat(Encoding.UTF8.GetBytes(text))).ToArray();

    /// <summary>An answer as mDNSResponder sends it: the question repeated, then compressed names throughout.</summary>
    private static byte[] Answer(string instance = "Studio Mac", int port = 47801, string host = "studio-mac.local", string? txt = "pin=" + Pin)
    {
        var message = new Message().Question(ComputerFinder.Service);
        var service = 12;
        var instanceAt = message.Offset;
        message.Record(Pointer(service), 12, new[] { (byte)Encoding.UTF8.GetByteCount(instance) }.Concat(Encoding.UTF8.GetBytes(instance)).Concat(Pointer(service)).ToArray());
        var instanceName = instanceAt + 12;
        var srvAt = message.Offset;
        message.Record(Pointer(instanceName), 33, Srv(port, Name(host)));
        var hostName = srvAt + 12 + 6;
        if (txt != null) message.Record(Pointer(instanceName), 16, Txt(txt));
        message.Record(Pointer(hostName), 1, new byte[] { 192, 168, 1, 20 });
        return message.Bytes();
    }

    [Test]
    public void TheQueryAsksOnceForTheServiceNameAndNothingElse()
    {
        var query = ComputerFinder.Query(ComputerFinder.Service);
        Assert.That(query.Take(12), Is.EqualTo(new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 }));
        Assert.That(query.Skip(12), Is.EqualTo(Name(ComputerFinder.Service).Concat(new byte[] { 0, 12, 0, 1 })), "a PTR question, class IN, no unicast-response bit needed from a port other than 5353");
    }

    [Test]
    public void AnAnswerNamesTheComputerItsAddressAndTheStartOfItsCertificate()
    {
        var found = ComputerFinder.Parse(Answer()).Single();
        Assert.That(found.Name, Is.EqualTo("Studio Mac"));
        Assert.That(found.Host, Is.EqualTo("studio-mac.local"));
        Assert.That(found.Port, Is.EqualTo(47801));
        Assert.That(found.Address, Is.EqualTo("studio-mac.local:47801"), "what pairing types for the person");
        Assert.That(found.Addresses, Is.EqualTo(new[] { "192.168.1.20" }));
        Assert.That(found.PinPrefix, Is.EqualTo(Pin));
    }

    [Test]
    public void AnAnswerWithoutAUsablePinIsStillOfferedButCarriesNone()
    {
        Assert.That(ComputerFinder.Parse(Answer(txt: null)).Single().PinPrefix, Is.Null);
        Assert.That(ComputerFinder.Parse(Answer(txt: "pin=NOT-HEX-AT-ALL!")).Single().PinPrefix, Is.Null);
    }

    [Test]
    public void AnAnswerOutsideTheLocalNetworkOrWithoutAPortIsNotOffered()
    {
        Assert.That(ComputerFinder.Parse(Answer(host: "studio.example.com")), Is.Empty, "only a .local host is the computer beside you");
        Assert.That(ComputerFinder.Parse(Answer(port: 0)), Is.Empty);
    }

    [Test]
    public void AMalformedAnswerIsDroppedWhole()
    {
        var answer = Answer();
        foreach (var length in new[] { 0, 5, 12, 30, answer.Length - 3 })
            Assert.That(ComputerFinder.Parse(answer.Take(length).ToArray()), Is.Empty, "cut at " + length);
        var looping = new Message().Record(Pointer(12), 12, Pointer(12)).Bytes();
        Assert.That(ComputerFinder.Parse(looping), Is.Empty, "a name that points at itself ends, it does not loop");
    }

    [Test]
    public async Task TheFinderAsksAndCollectsEachComputerThatAnswersOnce()
    {
        using var responder = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var asked = new TaskCompletionSource<byte[]>();
        var answering = Task.Run(async () =>
        {
            var query = await responder.ReceiveAsync();
            asked.TrySetResult(query.Buffer);
            foreach (var answer in new[] { Answer(), Answer(), Answer("Attic Mac", 47802, "attic.local") })
                await responder.SendAsync(answer, answer.Length, query.RemoteEndPoint);
        });
        var responderAt = (IPEndPoint)responder.Client.LocalEndPoint!;
        var found = await ComputerFinder.FindAsync(TimeSpan.FromMilliseconds(600), responderAt, CancellationToken.None);
        await answering;
        Assert.That((await asked.Task).Skip(12), Is.EqualTo(Name(ComputerFinder.Service).Concat(new byte[] { 0, 12, 0, 1 })));
        Assert.That(found.Select(computer => computer.Address), Is.EqualTo(new[] { "attic.local:47802", "studio-mac.local:47801" }));
        Assert.That(found.Any(computer => computer.SharesItsName), Is.False);
    }

    [Test]
    public async Task AnotherAnswerUnderTheSameNameIsShownBesideItNeverInsteadOfIt()
    {
        using var responder = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var answering = Task.Run(async () =>
        {
            var query = await responder.ReceiveAsync();
            foreach (var answer in new[] { Answer(), Answer(host: "pretender.local", txt: "pin=ffffffffffffffff"), Answer(port: 47999) })
                await responder.SendAsync(answer, answer.Length, query.RemoteEndPoint);
        });
        var found = await ComputerFinder.FindAsync(TimeSpan.FromMilliseconds(600), (IPEndPoint)responder.Client.LocalEndPoint!, CancellationToken.None);
        await answering;
        Assert.That(found.Select(computer => computer.Address), Is.EqualTo(new[] { "pretender.local:47801", "studio-mac.local:47801", "studio-mac.local:47999" }), "a later answer never replaces the person's own computer");
        Assert.That(found.All(computer => computer.SharesItsName && computer.Name == "Studio Mac"), Is.True, "the panel can say two answered as Studio Mac");
    }

    [Test]
    public async Task NobodyAnsweringIsNoFailure()
    {
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var found = await ComputerFinder.FindAsync(TimeSpan.FromMilliseconds(200), (IPEndPoint)silent.Client.LocalEndPoint!, CancellationToken.None);
        Assert.That(found, Is.Empty, "the person types the name, as without discovery");
    }

    /// <summary>
    /// Against macOS's own mDNSResponder on this Mac's network: run by hand with a pairing window open on
    /// the Mac (or `dns-sd -R` announcing the service), never in the suite.
    /// </summary>
    [Test, Explicit("Needs a computer announcing on this network")]
    public async Task ARealComputerAnnouncingIsFound()
    {
        var found = await ComputerFinder.FindAsync(TimeSpan.FromSeconds(2));
        TestContext.Out.WriteLine(found.Count + " found");
        Assert.That(found, Is.Not.Empty);
        Assert.That(found.All(computer => computer.Host.EndsWith(".local", StringComparison.Ordinal) && computer.Port > 0));
    }
}
