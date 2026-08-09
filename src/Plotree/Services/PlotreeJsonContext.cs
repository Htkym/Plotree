using System.Text.Json.Serialization;
using Plotree.Models;

namespace Plotree.Services;

/// <summary>Source-generated JSON metadata so Release trimming can't break (de)serialization.</summary>
[JsonSerializable(typeof(PlotProject))]
[JsonSerializable(typeof(RecentFilesService.AppSettings))]
internal sealed partial class PlotreeJsonContext : JsonSerializerContext;
