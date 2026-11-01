// NavigationConversationTests.cs
// Options > Gameplay "Game runs in conversations" (Settings.PlayOnInConversations, docs/notes/options.md): off (the default)
// a story or freelance conversation pauses the game as the original does; on, the clock runs and only the controls stop
// (Navigation.ConversationRuns -> InputHalted), the story level holding meanwhile (CampaignLevel.Held).

using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class NavigationConversationTests
    {
        GameObject go;
        Navigation nav;
        DataUiTestKit.PrefsSnapshot prefs;

        [SetUp]
        public void SetUp()
        {
            prefs = new DataUiTestKit.PrefsSnapshot();
            Settings.Reset(new[] { "playOnInConversations" });
            Time.timeScale = 1f;
            go = new GameObject("NavigationConversationTests");
            nav = go.AddComponent<Navigation>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(go);
            Time.timeScale = 1f;
            AudioListener.pause = false;
            prefs?.Dispose();
        }

        [Test]
        public void OffByDefaultAConversationPausesTheGame()
        {
            Assert.IsFalse(Settings.PlayOnInConversations, "the original's pause unless the player turns it on");
            nav.Conversation = true;
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsFalse(Navigation.ConversationRuns);
            nav.Conversation = false;
            Assert.AreEqual(1f, Time.timeScale);
        }

        [Test]
        public void OnTheGameRunsAndOnlyTheControlsStop()
        {
            Settings.PlayOnInConversations = true;
            nav.Conversation = true;
            Assert.AreEqual(1f, Time.timeScale, "the world keeps moving");
            Assert.IsTrue(Navigation.ConversationRuns);
            Assert.IsTrue(Navigation.InputHalted, "steering, guns and boost wait for the conversation, as in multiplayer");
            Assert.IsFalse(AudioListener.pause);
            nav.Conversation = false;
            Assert.IsFalse(Navigation.ConversationRuns);
            Assert.IsFalse(Navigation.InputHalted);
        }

        [Test]
        public void TheStarMapAndThePauseMenuStillPause()
        {
            Settings.PlayOnInConversations = true;
            nav.Paused = true;   // the jumpgate's star map: not a conversation
            Assert.AreEqual(0f, Time.timeScale);
            nav.Paused = false;
            nav.Conversation = true;
            nav.PauseMenuOpen = true;
            Assert.AreEqual(0f, Time.timeScale, "the pause menu over a conversation");
            nav.PauseMenuOpen = false;
            Assert.AreEqual(1f, Time.timeScale);
        }
    }
}
