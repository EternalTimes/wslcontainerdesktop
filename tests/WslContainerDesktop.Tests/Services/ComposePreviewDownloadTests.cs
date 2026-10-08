// WSL Container Desktop - a WinUI 3 manager for WSL containers.
// Copyright (C) 2026 Michael Hacker
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using WslContainerDesktop.Models;
using WslContainerDesktop.Services;
using WslContainerDesktop.ViewModels;
using Xunit;

namespace WslContainerDesktop.Tests.Services;

[Collection("UiText")]
public sealed class ComposePreviewDownloadTests : IDisposable
{
    private readonly Dictionary<string, string> _english = ReadCatalog("en-US");
    private readonly Dictionary<string, string> _chinese = ReadCatalog("zh-Hans");

    [Theory]
    [InlineData("en-US", ComposeServiceAction.Create)]
    [InlineData("zh-Hans", ComposeServiceAction.Create)]
    [InlineData("en-US", ComposeServiceAction.Recreate)]
    [InlineData("zh-Hans", ComposeServiceAction.Recreate)]
    public void ActualProjectionCountsDistinctDownloadsInBothLanguages(string language, ComposeServiceAction action)
    {
        ConfigureLanguage(language);
        var web = Service("web", "registry.invalid/shared:latest");
        var worker = Service("worker", web.Options.Image);
        var db = Service("db", "registry.invalid/db:latest");
        var cached = Service("cached", "registry.invalid/cached:latest");
        var built = Service("built", "registry.invalid/built:latest");
        var project = new ComposeProject { Name = "demo", Services = [web, worker, db, cached, built] };
        var plan = new ComposeReconciliationPlan([
            Entry(web, ComposeImageAction.Pull, action) with { DesiredReplicas = 2 },
            Entry(web, ComposeImageAction.Pull, action) with { DesiredReplicas = 2, InstanceIndex = 2, ContainerName = "demo_web_2" },
            Entry(worker, ComposeImageAction.Pull, action),
            Entry(db, ComposeImageAction.Pull, action),
            Entry(cached, ComposeImageAction.None, ComposeServiceAction.Keep),
            Entry(built, ComposeImageAction.Build, action),
        ]);
        var preview = new ComposePreviewProjection(project).Create(project, new(), plan);
        var rawEvidence = JsonSerializer.Serialize(preview);
        var model = new ComposePreviewViewModel(preview);

        Assert.Contains(language == "zh-Hans"
            ? "下载 2 个镜像（registry.invalid/shared:latest, registry.invalid/db:latest）"
            : "downloads 2 images (registry.invalid/shared:latest, registry.invalid/db:latest)", model.Outcome);
        Assert.DoesNotContain(cached.Options.Image, model.Outcome);
        Assert.DoesNotContain(built.Options.Image, model.Outcome);
        Assert.Same(preview.Settings, model.Settings);
        Assert.Equal(rawEvidence, JsonSerializer.Serialize(preview));
    }

    [Theory]
    [InlineData("en-US", ComposeImageAction.None)]
    [InlineData("zh-Hans", ComposeImageAction.None)]
    [InlineData("en-US", ComposeImageAction.Build)]
    [InlineData("zh-Hans", ComposeImageAction.Build)]
    public void CachedAndBuiltImagesAreNotAnnouncedAsDownloads(string language, ComposeImageAction imageAction)
    {
        ConfigureLanguage(language);
        var service = Service("web", "registry.invalid/image:latest");
        var project = new ComposeProject { Name = "demo", Services = [service] };
        var plan = new ComposeReconciliationPlan([Entry(service, imageAction, ComposeServiceAction.Create)]);
        var preview = new ComposePreviewProjection(project).Create(project, new(), plan);

        var outcome = new ComposePreviewViewModel(preview).Outcome;

        Assert.DoesNotContain(language == "zh-Hans" ? "下载" : "downloads", outcome);
        Assert.DoesNotContain(service.Options.Image, outcome);
    }

    private void ConfigureLanguage(string language)
    {
        var catalog = language == "zh-Hans" ? _chinese : _english;
        UiText.SetLanguage(language);
        UiText.Configure((key, args) => catalog.TryGetValue(key, out var value)
                ? string.Format(CultureInfo.InvariantCulture, value, args) : key,
            _english.Select(pair => (pair.Key, pair.Value, _chinese[pair.Key])));
    }

    private static ComposeService Service(string name, string image) => new()
    {
        Name = name, Options = new() { Image = image },
    };

    private static ComposeServicePlan Entry(ComposeService service, ComposeImageAction imageAction,
        ComposeServiceAction action) => new(service, "demo_" + service.Name, "",
        action == ComposeServiceAction.Recreate ? ComposeServiceChange.Changed : ComposeServiceChange.Missing,
        action, "Fixture instance.", action == ComposeServiceAction.Create ? null : "existing_" + service.Name)
    {
        ImageAction = imageAction, Backend = ComposeExecutionBackend.LegacyRun, HealthOwner = ComposePolicyOwner.None,
    };

    private static Dictionary<string, string> ReadCatalog(string language)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WslContainerDesktop.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return XDocument.Load(Path.Combine(directory.FullName, "src", "WslContainerDesktop", "Strings", language, "Resources.resw"))
            .Root!.Elements("data").ToDictionary(element => element.Attribute("name")!.Value,
                element => element.Element("value")!.Value, StringComparer.Ordinal);
    }

    public void Dispose()
    {
        UiText.Configure(null);
        UiText.SetLanguage("en-US");
    }
}
