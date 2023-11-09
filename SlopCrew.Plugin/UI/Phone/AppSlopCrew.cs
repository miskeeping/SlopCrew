using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using Reptile;
using Reptile.Phone;
using SlopCrew.Common;
using SlopCrew.Common.Network.Serverbound;
using SlopCrew.Plugin.Encounters;
using SlopCrew.Plugin.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SlopCrew.Plugin.UI.Phone;

public class AppSlopCrew : App {
    [NonSerialized] public string? RaceRankings;
    [NonSerialized] public TMP_Text Label = null!;

    private SlopCrewScrollView? scrollView;

    private AssociatedPlayer? nearestPlayer;
    private EncounterType encounter = EncounterType.ScoreEncounter;

    private List<EncounterType> encounterTypes = new() {
        EncounterType.ScoreEncounter,
        EncounterType.ComboEncounter,
        EncounterType.RaceEncounter
    };

    private bool notifInitialized;
    private bool playerLocked;
    private bool isWaitingForARace = false;

    public override void Awake() {
        this.m_Unlockables = Array.Empty<AUnlockable>();
        base.Awake();
    }

    protected override void OnAppInit() {
        var contentObject = new GameObject("Content");
        contentObject.layer = Layers.Phone;
        contentObject.transform.SetParent(transform, false);
        contentObject.transform.localScale = Vector3.one;

        var content = contentObject.AddComponent<RectTransform>();
        content.sizeDelta = new(1070, 1775);

        AddScrollView(content);
        AddOverlay(content);
    }

    private void AddOverlay(RectTransform content) {
        AppGraffiti graffitiApp = this.MyPhone.GetAppInstance<AppGraffiti>();

        GameObject overlay = graffitiApp.transform.GetChild(1).gameObject;
        GameObject slopOverlay = Instantiate(overlay, content);

        var title = slopOverlay.GetComponentInChildren<TextMeshProUGUI>();
        Destroy(title.GetComponent<TMProLocalizationAddOn>());
        Destroy(title.GetComponent<TMProFontLocalizer>());
        title.SetText("Slop Crew");

        var overlayHeaderImage = slopOverlay.transform.GetChild(0);
        overlayHeaderImage.localPosition = Vector2.up * 870.0f;

        var iconImage = slopOverlay.transform.GetChild(1).GetChild(1).GetComponent<Image>();
        iconImage.sprite = TextureLoader.LoadResourceAsSprite("SlopCrew.Plugin.res.phone_icon.png", 128, 128);
    }

    private void AddScrollView(RectTransform content) {
        AppMusicPlayer musicApp = this.MyPhone.GetAppInstance<AppMusicPlayer>();

        // I really just do not want to hack together custom objects for sprites the game already loads anyway
        var musicTraverse = Traverse.Create(musicApp);
        var musicList = musicTraverse.Field("m_TrackList").GetValue() as MusicPlayerTrackList;
        var musicListTraverse = Traverse.Create(musicList);
        var musicButtonPrefab = musicListTraverse.Field("m_AppButtonPrefab").GetValue() as GameObject;

        var confirmArrow = musicButtonPrefab.transform.Find("PromptArrow");
        var titleLabel = musicButtonPrefab.transform.Find("TitleLabel").GetComponent<TextMeshProUGUI>();

        var scrollViewObject = new GameObject("Buttons");
        scrollViewObject.layer = Layers.Phone;
        var scrollViewRect = scrollViewObject.AddComponent<RectTransform>();
        scrollViewRect.SetParent(content, false);
        scrollView = scrollViewObject.AddComponent<SlopCrewScrollView>();
        scrollView.Initialize(this, confirmArrow.gameObject, titleLabel);
        scrollView.InitalizeScrollView();
    }

    public override void OnAppEnable() {
        base.OnAppEnable();

        scrollView.SetListContent(encounterTypes.Count);
    }

    public override void OnPressUp() {
        if (this.RaceRankings is not null) {
            this.RaceRankings = null;
            return;
        }
        if (Plugin.CurrentEncounter?.IsBusy == true) return;

        var nextIndex = this.encounterTypes.IndexOf(this.encounter) - 1;
        if (nextIndex < 0) nextIndex = this.encounterTypes.Count - 1;

        scrollView.Move(PhoneScroll.ScrollDirection.Previous, m_AudioManager);

        var nextEncounter = encounterTypes[nextIndex];

        this.encounter = nextEncounter;
    }

    public override void OnPressDown() {
        if (this.RaceRankings is not null) {
            this.RaceRankings = null;
            return;
        }
        if (Plugin.CurrentEncounter?.IsBusy == true) return;

        var nextIndex = this.encounterTypes.IndexOf(this.encounter) + 1;
        if (nextIndex >= this.encounterTypes.Count) nextIndex = 0;

        scrollView.Move(PhoneScroll.ScrollDirection.Next, m_AudioManager);

        var nextEncounter = encounterTypes[nextIndex];

        this.encounter = nextEncounter;
    }

    public override void OnPressRight() {
        scrollView.HoldAnimationSelectedButton();

        //if (isWaitingForARace) {
        //    this.SendCancelEncounterRequest();
        //    return;
        //}

        //if (this.RaceRankings is not null) {
        //    this.RaceRankings = null;
        //    return;
        //}
        //if (!this.SendEncounterRequest()) return;

        //// People wanted an audible sound so you'll get one
        //var audioManager = Core.Instance.AudioManager;
        //var playSfx = AccessTools.Method("Reptile.AudioManager:PlaySfxGameplay",
        //                                 new[] { typeof(SfxCollectionID), typeof(AudioClipID), typeof(float) });
        //playSfx.Invoke(audioManager, new object[] { SfxCollectionID.PhoneSfx, AudioClipID.FlipPhone_Confirm, 0f });

        //if (this.encounter is EncounterType.RaceEncounter && !isWaitingForARace) {
        //    isWaitingForARace = true;
        //}
    }

    public override void OnReleaseRight() {
        scrollView.ActivateAnimationSelectedButton();
    }

    private bool SendEncounterRequest() {
        if (!this.encounter.IsStateful() && this.nearestPlayer == null) return false;
        if (Plugin.CurrentEncounter?.IsBusy == true) return false;
        if (this.HasBannedMods()) return false;

        Plugin.HasEncounterBeenCancelled = false;

        Plugin.NetworkConnection.SendMessage(new ServerboundEncounterRequest {
            PlayerID = this.nearestPlayer?.SlopPlayer.ID ?? uint.MaxValue,
            EncounterType = this.encounter
        });

        return true;
    }

    private void SendCancelEncounterRequest() {
        Plugin.NetworkConnection.SendMessage(new ServerboundEncounterCancel {
            EncounterType = this.encounter
        });
    }

    public override void OnAppUpdate() {
        return;

        var me = WorldHandler.instance.GetCurrentPlayer();
        if (me is null) return;

        if (Plugin.CurrentEncounter is SlopRaceEncounter && isWaitingForARace) {
            isWaitingForARace = false;
        }

        if (isWaitingForARace) {
            if (Plugin.HasEncounterBeenCancelled) {
                isWaitingForARace = false;

                Core.Instance.AudioManager.PlaySfx(SfxCollectionID.PhoneSfx, AudioClipID.FlipPhone_Confirm);
            }

            this.Label.text = "Waiting for a race...\n Press right to cancel";
            return;
        }

        if (this.HasBannedMods()) {
            this.Label.text = "Please disable\nadvantageous\nmods";
            return;
        }

        if (Plugin.CurrentEncounter?.IsBusy == true) {
            if (Plugin.CurrentEncounter is SlopRaceEncounter race && race.IsWaitingForResults()) {
                this.Label.text = "Waiting for results...";
            } else {
                this.Label.text = "glhf";
            }
            return;
        }

        if (this.RaceRankings is not null) {
            this.Label.text = this.RaceRankings;
            return;
        }

        if (!this.playerLocked) {
            var position = me.transform.position.FromMentalDeficiency();
            this.nearestPlayer = Plugin.PlayerManager.AssociatedPlayers
                .Where(x => x.IsValid())
                .OrderBy(x =>
                             System.Numerics.Vector3.Distance(
                                 x.ReptilePlayer.transform.position.FromMentalDeficiency(),
                                 position
                             ))
                .FirstOrDefault();
        }

        var modeName = this.encounter switch {
            EncounterType.ScoreEncounter => "score",
            EncounterType.ComboEncounter => "combo",
            EncounterType.RaceEncounter => "race"
        };

        if (this.encounter.IsStateful()) {
            this.Label.text = $"Press right\nto wait for a\n{modeName} battle";
            return;
        }

        if (this.nearestPlayer == null) {
            if (this.playerLocked) this.playerLocked = false;
            this.Label.text = $"No players nearby\nfor {modeName} battle";
        } else {
            var filteredName = PlayerNameFilter.DoFilter(this.nearestPlayer.SlopPlayer.Name);
            var text = $"Press right\nto {modeName} battle\n" + filteredName;

            if (this.playerLocked) {
                text = $"{filteredName}<color=white>\nwants to {modeName} battle!\n\nPress right\nto start";
            }

            this.Label.text = text;
        }
    }

    private bool HasBannedMods() {
        var bannedMods = Plugin.NetworkConnection.ServerConfig?.BannedMods ?? new();
        return Chainloader.PluginInfos.Keys.Any(x => bannedMods.Contains(x));
    }

    public void EndWaitingForRace() {
        isWaitingForARace = false;
    }

    public void SetNotification(Notification notif) {
        if (this.notifInitialized) return;
        var newNotif = Instantiate(notif.gameObject, this.transform);
        this.m_Notification = newNotif.GetComponent<Notification>();
        this.m_Notification.InitNotification(this);
        this.notifInitialized = true;
    }

    public override void OpenContent(AUnlockable unlockable, bool appAlreadyOpen) {
        if (Plugin.PhoneInitializer.LastRequest is not null) {
            var request = Plugin.PhoneInitializer.LastRequest;
            this.encounter = request.EncounterType;

            if (Plugin.PlayerManager.Players.TryGetValue(request.PlayerID, out var player)) {
                this.nearestPlayer = player;
                if (Plugin.SlopConfig.StartEncountersOnRequest.Value) {
                    this.SendEncounterRequest();
                } else {
                    this.playerLocked = true;
                    Task.Run(() => {
                        Task.Delay(5000).Wait();
                        this.playerLocked = false;
                    });
                }
            }
        }

        Plugin.PhoneInitializer.LastRequest = null;
    }
}
