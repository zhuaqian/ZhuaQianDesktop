using System;
using System.Collections.Generic;
using ZhuaQianDesktopApp.Agent;

// Validates SiteGenerator.ParseFiles splits a model reply into {filename, content}
// by reading fenced code blocks. No real model required.
static class TestSiteGenerator
{
    public static int RunAll()
    {
        int failures = 0;
        failures += TestParseFenced();
        failures += TestParseNoFence();
        Console.WriteLine("[TestSiteGenerator] failures=" + failures);
        return failures;
    }

    static int TestParseFenced()
    {
        int fails = 0;
        string reply = "Here is your site:\n```html\n<html><body>Hi</body></html>\n```\n```css\nbody{color:red}\n```\n```js\nconsole.log(1)\n```";
        var files = SiteGenerator.ParseFiles(reply);
        Assert(files.Count == 3, "three files parsed, got " + files.Count);
        Assert(files[0].Path == "index.html", "first is index.html, got " + files[0].Path);
        Assert(files[1].Path == "style.css", "second is style.css");
        Assert(files[2].Path == "app.js", "third is app.js");
        return fails;
    }

    static int TestParseNoFence()
    {
        int fails = 0;
        var files = SiteGenerator.ParseFiles("just some text");
        Assert(files.Count == 1 && files[0].Path == "index.html", "no-fence falls back to index.html");
        return fails;
    }
}
