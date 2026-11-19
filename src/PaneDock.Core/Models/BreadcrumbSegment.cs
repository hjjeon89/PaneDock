namespace PaneDock.Core.Models;

/// <summary>One clickable ancestor in a file-system path.</summary>
public sealed record BreadcrumbSegment(string Label, string FullPath);
