using System;
using System.Collections.Generic;
using System.Linq;
using Aniplayer.Core.Models;

namespace Aniplayer.Core.Helpers;

public static class Chapters
{
    public record ChapterInfo(string Title, double Time);

    public static void Detect(Episode episode, List<ChapterInfo> chapters, double duration)
    {
        if (episode == null || chapters == null || !chapters.Any()) return;

        episode.IntroStart = -1;
        episode.IntroEnd = -1;
        episode.OutroStart = -1;
        episode.OutroEnd = -1;

        for (int i = 0; i < chapters.Count; i++)
        {
            var current = chapters[i];
            var title = current.Title?.ToLowerInvariant() ?? "";
            double endTime = (i + 1 < chapters.Count) ? chapters[i + 1].Time : duration;

            if (IsIntro(title))
            {
                episode.IntroStart = current.Time;
                episode.IntroEnd = endTime;
            }

            if (IsOutro(title))
            {
                episode.OutroStart = current.Time;
                episode.OutroEnd = endTime;
            }
        }
    }

    private static bool IsIntro(string title)
    {
        return title.Contains("intro") || title.Contains("opening") || 
               title.Equals("op") || title.StartsWith("op ");
    }

    private static bool IsOutro(string title)
    {
        return title.Contains("ending") || title.Contains("outro") || 
               title.Equals("ed") || title.StartsWith("ed ");
    }
}
