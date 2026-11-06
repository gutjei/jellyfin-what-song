using System.Threading.Channels;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SongPause;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class SessionSongNotifierTests
{
    private sealed class Rig : IAsyncDisposable
    {
        public readonly Mock<ISessionManager> Sessions = new();
        public readonly Mock<ILibraryManager> Library = new();
        public readonly Mock<IUserManager> Users = new();
        public readonly WebClientRegistry Web = new();
        public readonly Channel<(string Session, MessageCommand Message)> Messages = Channel.CreateUnbounded<(string, MessageCommand)>();
        public readonly Movie Movie;
        public readonly SessionInfo Session;
        public readonly SessionSongNotifier Service;
        public readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "song-pause-tests-" + Guid.NewGuid());
        public bool FailNextSend;

        public Rig(string client = "Jellyfin Android TV")
        {
            Directory.CreateDirectory(DirectoryPath);
            Movie = new Movie { Id = Guid.NewGuid(), Path = Path.Combine(DirectoryPath, "Movie.mkv") };
            WriteTimeline();
            var user = new User("test", "auth", "reset") { Id = Guid.NewGuid() };
            Users.Setup(x => x.GetUserById(user.Id)).Returns(user);
            Library.Setup(x => x.GetItemById<BaseItem>(Movie.Id, user)).Returns(Movie);
            var controller = new Mock<ISessionController>();
            controller.SetupGet(x => x.SupportsMediaControl).Returns(true);
            controller.SetupGet(x => x.IsSessionActive).Returns(true);
            Session = new SessionInfo(Sessions.Object, NullLogger.Instance)
            {
                Id = Guid.NewGuid().ToString("N"), UserId = user.Id, Client = client,
                Capabilities = new ClientCapabilities { SupportsMediaControl = true, SupportedCommands = [GeneralCommandType.DisplayMessage] },
                SessionControllers = [controller.Object]
            };
            Sessions.Setup(x => x.SendMessageCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()))
                .Returns((string? _, string id, MessageCommand message, CancellationToken token) =>
                {
                    if (FailNextSend) { FailNextSend = false; throw new IOException("Disconnected"); }
                    Messages.Writer.TryWrite((id, message));
                    return Task.CompletedTask;
                });
            Service = new SessionSongNotifier(Sessions.Object, Library.Object, Users.Object, Web, NullLogger<SessionSongNotifier>.Instance);
        }

        public void WriteTimeline(string? json = null) => File.WriteAllText(Path.ChangeExtension(Movie.Path, ".songs.json"), json ?? """
            {"version":1,"songs":[
              {"startMs":5000,"endMs":20000,"title":"First","artist":"Artist"},
              {"startMs":18000,"endMs":32000,"title":"Second"}
            ]}
            """);

        public Task Start() => Service.StartAsync(CancellationToken.None);
        public void Progress(long ms = 10000, bool paused = true, bool automated = false, string? source = null, SessionInfo? session = null)
            => Sessions.Raise(x => x.PlaybackProgress += null, new PlaybackProgressEventArgs
            {
                Session = session ?? Session, Item = Movie, IsPaused = paused, IsAutomated = automated,
                PlaybackPositionTicks = ms * TimeSpan.TicksPerMillisecond, MediaSourceId = source ?? Movie.Id.ToString("N")
            });
        public async Task<MessageCommand> Next(string? sessionId = null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var value = await Messages.Reader.ReadAsync(timeout.Token);
            Assert.Equal(sessionId ?? Session.Id, value.Session);
            return value.Message;
        }
        public async Task None()
        {
            await Task.Delay(650);
            Assert.False(Messages.Reader.TryRead(out _));
        }
        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync(CancellationToken.None);
            Service.Dispose();
            await Session.DisposeAsync();
            // Only this fixture's newly created temporary directory is removed.
            Directory.Delete(DirectoryPath, true);
        }
    }

    [Theory]
    [InlineData("Jellyfin Android TV")]
    [InlineData("VLC adapter")]
    [InlineData("MPV adapter")]
    [InlineData("Unknown future client")]
    public async Task PauseAndSeekUseSessionCapabilitiesNotPlayerNames(string client)
    {
        await using var rig = new Rig(client);
        await rig.Start();
        rig.Progress();
        Assert.Equal("Artist — First", (await rig.Next()).Text);
        rig.Progress(19000);
        Assert.Equal("Artist — First\nSecond", (await rig.Next()).Text);
        rig.Progress(19000);
        await rig.None();
        rig.Progress(paused: false);
        rig.Progress(19000);
        Assert.Contains("Second", (await rig.Next()).Text);
        rig.Progress(32000);
        await rig.None();
    }

    [Theory]
    [InlineData("resume")]
    [InlineData("stop")]
    [InlineData("end")]
    [InlineData("web")]
    public async Task PendingNotificationsAreInvalidated(string action)
    {
        await using var rig = new Rig();
        await rig.Start();
        rig.Progress();
        switch (action)
        {
            case "resume": rig.Progress(paused: false); break;
            case "stop": rig.Sessions.Raise(x => x.PlaybackStopped += null, new PlaybackStopEventArgs { Session = rig.Session }); break;
            case "end": rig.Sessions.Raise(x => x.SessionEnded += null, new SessionEventArgs { SessionInfo = rig.Session }); break;
            case "web": rig.Web.Register(rig.Session.Id); break;
        }
        await rig.None();
    }

    [Fact]
    public async Task RapidSeeksOnlySendLatestPosition()
    {
        await using var rig = new Rig();
        await rig.Start();
        rig.Progress(); rig.Progress(19000); rig.Progress(25000);
        Assert.Equal("Second", (await rig.Next()).Text);
        await rig.None();
    }

    [Fact]
    public async Task UnsupportedOrDisconnectedClientsReceiveNothing()
    {
        await using var rig = new Rig();
        await rig.Start();
        rig.Session.Capabilities.SupportedCommands = [];
        rig.Progress();
        await rig.None();
        rig.Session.Capabilities.SupportedCommands = [GeneralCommandType.DisplayMessage];
        rig.Session.SessionControllers = [];
        rig.Progress();
        await rig.None();
    }

    [Fact]
    public async Task AutomatedProgressAndWebClientsReceiveNoDuplicate()
    {
        await using var rig = new Rig();
        await rig.Start();
        rig.Progress(automated: true);
        await rig.None();
        rig.Web.Register(rig.Session.Id);
        rig.Progress();
        await rig.None();
    }

    [Theory]
    [InlineData("not-a-library-id")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task UnknownMediaSourceNeverFallsBackToWrongEdition(string source)
    {
        await using var rig = new Rig();
        await rig.Start();
        rig.Progress(source: source);
        await rig.None();
    }

    [Fact]
    public async Task ReadsSelectedEditionTimeline()
    {
        await using var rig = new Rig();
        var alternate = new Movie { Id = Guid.NewGuid(), Path = Path.Combine(rig.DirectoryPath, "Alternate.mkv") };
        File.WriteAllText(Path.ChangeExtension(alternate.Path, ".songs.json"), """{"version":1,"songs":[{"startMs":0,"endMs":20000,"title":"Alternate"}]}""");
        rig.Library.Setup(x => x.GetItemById<BaseItem>(alternate.Id, It.IsAny<User>())).Returns(alternate);
        await rig.Start();
        rig.Progress(source: alternate.Id.ToString("N"));
        Assert.Equal("Alternate", (await rig.Next()).Text);
    }

    [Fact]
    public async Task InvalidTimelineAndFailedDeliveryCanRecover()
    {
        await using var rig = new Rig();
        rig.WriteTimeline("{invalid");
        await rig.Start(); rig.Progress();
        await rig.None();
        rig.WriteTimeline();
        rig.FailNextSend = true;
        rig.Progress();
        await rig.None();
        rig.Progress();
        Assert.Equal("Artist — First", (await rig.Next()).Text);
    }

    [Fact]
    public async Task MissingFileProducesNoNotification()
    {
        await using var rig = new Rig();
        File.Delete(Path.ChangeExtension(rig.Movie.Path, ".songs.json"));
        await rig.Start(); rig.Progress();
        await rig.None();
    }

    [Fact]
    public async Task SessionsRemainIsolated()
    {
        await using var rig = new Rig();
        var second = new SessionInfo(rig.Sessions.Object, NullLogger.Instance)
        {
            Id = "second", UserId = rig.Session.UserId, Capabilities = rig.Session.Capabilities,
            SessionControllers = rig.Session.SessionControllers
        };
        await rig.Start();
        rig.Progress(); rig.Progress(25000, session: second);
        Assert.Equal("Artist — First", (await rig.Next()).Text);
        Assert.Equal("Second", (await rig.Next("second")).Text);
        await second.DisposeAsync();
    }
}
