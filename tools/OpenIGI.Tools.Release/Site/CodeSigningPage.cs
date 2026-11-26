namespace OpenIGI.Tools.Release;

/// <summary>
/// <c>/code-signing/</c>: the code signing policy SignPath Foundation asks every project it signs for to publish: who
/// signs what, how a release reaches a signature, and what the signed programs send over the network. It must stay true
/// to <c>.github/workflows/release.yml</c> and <c>tools/release/signpath/release.xml</c>.
/// </summary>
internal static class CodeSigningPage
{
    /// <summary>The page.</summary>
    public static string Html { get; } = TextPage.Html(
        "Code signing",
        "How OpenIGI's Windows downloads are signed, by whom, and what the signed programs send over the network.",
        """
            <p>Free code signing provided by <a href="https://about.signpath.io">SignPath.io</a>, certificate by
              <a href="https://signpath.org">SignPath Foundation</a>.</p>
            <p>So Windows can show who made it, every OpenIGI program for Windows carries an Authenticode signature: the game, the
              launcher and the dedicated server, on <a href="https://github.com/OpenIGI/OpenIGI/releases">GitHub</a> and on this site.
              Windows shows the publisher as SignPath Foundation.</p>

            <h2>What gets signed</h2>
            <p>Only programs built from this project's own source code in its public repository,
              <a href="https://github.com/OpenIGI/OpenIGI">github.com/OpenIGI/OpenIGI</a>. Each release is built on GitHub's
              own build machines by the release workflow (<code>.github/workflows/release.yml</code>), from a tagged commit
              on the release's branch, and the files that workflow built are the files that get signed. Nothing built on a
              personal computer is ever signed. Third-party libraries in the dedicated server keep their own projects' signatures.</p>

            <h2>Who decides</h2>
            <ul>
              <li><b>Committers and reviewers</b>: the members of the <a href="https://github.com/orgs/OpenIGI/people">OpenIGI
                organisation on GitHub</a>. Changes from anyone else are reviewed by one of them before they are merged.</li>
              <li><b>Approvers</b>: the project's maintainers, the owners of that organisation. Each signing request is
                approved by one of them in SignPath, by hand, after checking it came from a release build.</li>
            </ul>
            <p>Every member of these roles signs in to GitHub and SignPath with two-factor authentication.</p>

            <h2>Privacy</h2>
            <p>The signed programs send nothing to other computers unless you ask them to, or the person who installed or runs them
              set them up to:</p>
            <ul>
              <li>The launcher checks openigi.com for updates and downloads the builds and packs you choose from openigi.com and GitHub.</li>
              <li>Server lists, multiplayer and co-op talk to the servers you pick and, to find them, to OpenIGI's master server.</li>
              <li>Signing in with Discord is optional. <a href="/privacy/">Privacy</a> lists everything kept when you do.</li>
            </ul>

            <h2>Report a problem</h2>
            <p>If a signed file looks wrong, or you think something was signed that should not have been, tell us through
              <a href="https://github.com/OpenIGI/OpenIGI/security/advisories/new">a private security report</a> on GitHub.</p>
        """);
}
