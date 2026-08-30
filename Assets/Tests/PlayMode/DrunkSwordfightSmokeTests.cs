using System.Reflection;
using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Headless smoke test for the whole drunk swordfight flow:
/// boot host -> launch minigame through the harness -> gear spawns ->
/// countdown unfreezes -> whiskey sips heal and stack drunk -> damage ->
/// death -> solo round resolves and the harness unloads the scene.
///
/// Run: Unity -batchmode -runTests -testPlatform PlayMode -projectPath &lt;repo&gt;
/// </summary>
public class DrunkSwordfightSmokeTests
{
    private const string SceneName = "MG_DrunkSwordfight";

    [UnityTest]
    public IEnumerator DrunkSwordfight_FullRoundResolves()
    {
        // bring up the Hub (NetworkManager + MinigameManager live there)
        SceneManager.LoadScene("Hub");
        yield return new WaitForSeconds(1f);

        var nm = NetworkManager.Singleton;
        Assert.IsNotNull(nm, "NetworkManager missing from Hub scene");
        nm.StartHost();
        yield return new WaitForSeconds(0.5f);

        var mm = MinigameManager.Instance;
        Assert.IsNotNull(mm, "MinigameManager missing");
        Assert.IsTrue(mm.IsSpawned);

        int index = IndexOf(mm, SceneName);
        Assert.GreaterOrEqual(index, 0, $"MinigameManager does not list {SceneName}");

        // launch and wait for the avatar + gear
        mm.LaunchMinigame(index);

        DrunkFightPlayer fighter = null;
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 30f)
        {
            yield return null;
            fighter = Object.FindAnyObjectByType<DrunkFightPlayer>();
            if (fighter != null && fighter.GearActive && fighter.IsSpawned) break;
        }
        Assert.IsNotNull(fighter, "No DrunkFightPlayer avatar spawned");
        Assert.IsTrue(fighter.GearActive, "Swordfight gear never activated");
        Assert.IsNotNull(fighter.GetComponentInChildren<SwordWielder>(true), "Sword missing");
        Assert.IsNotNull(fighter.transform.Find("WhiskeyBottle"), "Whiskey bottle missing");

        Assert.AreEqual(100f, fighter.Hp.Value, 0.01f);
        Assert.AreEqual(3, fighter.SipsLeft.Value);
        Assert.AreEqual(0, fighter.DrunkLevel.Value);
        Assert.IsTrue(fighter.Alive.Value);
        Assert.IsTrue(fighter.Frozen.Value, "should be frozen during countdown");

        // wait for "FIGHT!"
        t0 = Time.realtimeSinceStartup;
        while (fighter.Frozen.Value && Time.realtimeSinceStartup - t0 < 15f)
            yield return null;
        Assert.IsFalse(fighter.Frozen.Value, "countdown never ended");

        // sip whiskey (call the ServerRpc body directly; we ARE the host).
        // sips are locked for ~0.9s each, so wait between them.
        CallPrivate(fighter, "TrySipServerRpc");
        yield return new WaitForSeconds(1.0f);
        Assert.AreEqual(2, fighter.SipsLeft.Value, "sip should leave 2 sips");
        Assert.AreEqual(1, fighter.DrunkLevel.Value, "sip should add drunk level");
        Assert.AreEqual(100f, fighter.Hp.Value, 0.01f, "heal caps at max HP");

        // take a hit, then heal from the bottle
        fighter.HostTakeDamage(60f, Vector3.forward, 1);
        yield return null;
        Assert.AreEqual(40f, fighter.Hp.Value, 0.01f);

        CallPrivate(fighter, "TrySipServerRpc");
        yield return new WaitForSeconds(1.0f);
        Assert.AreEqual(75f, fighter.Hp.Value, 0.01f, "35 heal after damage");

        // drain the bottle: third sip empties it, then a dry pull does nothing
        CallPrivate(fighter, "TrySipServerRpc");
        yield return new WaitForSeconds(1.0f);
        Assert.AreEqual(0, fighter.SipsLeft.Value, "third sip empties the bottle");
        Assert.AreEqual(3, fighter.DrunkLevel.Value, "drunk level caps at 3");
        float hpBeforeDry = fighter.Hp.Value;
        CallPrivate(fighter, "TrySipServerRpc");
        yield return new WaitForSeconds(0.2f);
        Assert.AreEqual(hpBeforeDry, fighter.Hp.Value, 0.01f, "no heal from empty bottle");

        // solo round: kill the only fighter, the harness should end and clean up
        fighter.HostTakeDamage(999f, Vector3.forward, 1);
        yield return null;
        Assert.IsFalse(fighter.Alive.Value);

        t0 = Time.realtimeSinceStartup;
        while (mm.MinigameInProgress.Value && Time.realtimeSinceStartup - t0 < 20f)
            yield return null;
        Assert.IsFalse(mm.MinigameInProgress.Value, "minigame did not finish");
        Assert.IsFalse(SceneUtil.IsSceneLoaded(SceneName), "minigame scene did not unload");

        nm.Shutdown();
        yield return null;
    }

    private static int IndexOf(MinigameManager mm, string sceneName)
    {
        for (int i = 0; i < mm.MinigameScenes.Count; i++)
            if (mm.MinigameScenes[i] == sceneName) return i;
        return -1;
    }

    private static void CallPrivate<T>(T target, string method) where T : Object
    {
        var mi = typeof(T).GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(mi, $"method {method} not found on {typeof(T).Name}");
        mi.Invoke(target, null);
    }
}
