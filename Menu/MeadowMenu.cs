﻿using HarmonyLib;
using Menu;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static RainMeadow.MeadowProgression;

namespace RainMeadow
{
    public class MeadowMenu : SmartMenu, SelectOneButton.SelectOneButtonOwner
    {
        private Vector2 btns = new Vector2(350, 100);
        private Vector2 btnsize = new Vector2(100, 20);
        private RainEffect rainEffect;
        private List<SlugcatSelectMenu.SlugcatPage> characterPages;
        private List<MeadowProgression.Character> playableCharacters;
        private Dictionary<MeadowProgression.Character, List<MeadowProgression.Skin>> characterSkins;
        private EventfulHoldButton startButton;
        private EventfulBigArrowButton prevButton;
        private EventfulBigArrowButton nextButton;
        private SlugcatSelectMenu ssm;
        private EventfulSelectOneButton[] skinButtons;

        public override MenuScene.SceneID GetScene => null;
        public MeadowMenu(ProcessManager manager) : base(manager, RainMeadow.Ext_ProcessID.MeadowMenu)
        {
            RainMeadow.DebugMe();
            this.rainEffect = new RainEffect(this, this.pages[0]);
            this.pages[0].subObjects.Add(this.rainEffect);
            this.rainEffect.rainFade = 0.3f;
            this.characterPages = new List<SlugcatSelectMenu.SlugcatPage>();

            ssm = (SlugcatSelectMenu)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SlugcatSelectMenu));
            ssm.container = container;
            ssm.slugcatPages = characterPages;
            ssm.ID = ProcessManager.ProcessID.MultiplayerMenu;
            ssm.cursorContainer = cursorContainer;
            ssm.manager = manager;
            ssm.pages = pages;

            playableCharacters = MeadowProgression.instance.AllAvailableCharacters();
            characterSkins = new();
            for (int j = 0; j < this.playableCharacters.Count; j++)
            {
                this.characterPages.Add(new MeadowCharacterSelectPage(this, ssm, 1 + j, this.playableCharacters[j]));
                if (characterPages[j].sceneOffset.y == 0) characterPages[0].sceneOffset = new Vector2(-10f, 100f);
                this.pages.Add(this.characterPages[j]);

                var skins = MeadowProgression.instance.AllAvailableSkins(this.playableCharacters[j]);
                RainMeadow.Debug(skins);
                RainMeadow.Debug(skins.Select(s => s.ToString()).Aggregate((a, b) => (a + b)));
                characterSkins[playableCharacters[j]] = skins;
            }

            this.startButton = new EventfulHoldButton(this, this.pages[0], base.Translate("ENTER"), new Vector2(683f, 85f), 40f);
            this.startButton.OnClick += (_) => { StartGame(); };
            startButton.buttonBehav.greyedOut = !OnlineManager.lobby.isAvailable;
            OnlineManager.lobby.OnLobbyAvailable += OnLobbyAvailable;
            this.pages[0].subObjects.Add(this.startButton);
            this.prevButton = new EventfulBigArrowButton(this, this.pages[0], new Vector2(345f, 50f), -1);
            this.prevButton.OnClick += (_) => {
                ssm.quedSideInput = Math.Max(-3, ssm.quedSideInput - 1);
                base.PlaySound(SoundID.MENU_Next_Slugcat);
            };
            this.pages[0].subObjects.Add(this.prevButton);
            this.nextButton = new EventfulBigArrowButton(this, this.pages[0], new Vector2(985f, 50f), 1);
            this.nextButton.OnClick += (_) => {
                ssm.quedSideInput = Math.Min(3, ssm.quedSideInput + 1);
                base.PlaySound(SoundID.MENU_Next_Slugcat);
            };
            this.pages[0].subObjects.Add(this.nextButton);
            this.mySoundLoopID = SoundID.MENU_Main_Menu_LOOP;

            var skinLabel = new MenuLabel(this, mainPage, this.Translate("SKINS"), new Vector2(194, 553), new(110, 30), true);
            this.pages[0].subObjects.Add(skinLabel);
            UpdateCharacterUI();
        }

        private void UpdateCharacterUI()
        {
            if(skinButtons != null)
            {
                var oldSkinButtons = skinButtons;
                for (int i = 0; i < oldSkinButtons.Length; i++)
                {
                    var btn = oldSkinButtons[i];
                    btn.RemoveSprites();
                    mainPage.RemoveSubObject(btn);
                }
            }
            
            var skins = characterSkins[playableCharacters[ssm.slugcatPageIndex]];
            skinButtons = new EventfulSelectOneButton[skins.Count];
            for (int i = 0; i < skins.Count; i++)
            {
                var skin = skins[i];
                var btn = new EventfulSelectOneButton(this, mainPage, MeadowProgression.Skin.skinNames[skin], "skinButtons", new Vector2(194, 515) - i * new Vector2(0, 38), new(110, 30), skinButtons, i);
                mainPage.subObjects.Add(btn);
                skinButtons[i] = btn;
            }
        }

        public override void Update()
        {
            base.Update();
            if (this.rainEffect != null)
            {
                this.rainEffect.rainFade = Mathf.Min(0.3f, this.rainEffect.rainFade + 0.006f);
            }

            ssm.lastScroll = ssm.scroll;
            ssm.scroll = ssm.NextScroll;
            // todo check if we need this
            ////this.startButton.GetButtonBehavior.greyedOut = (Mathf.Abs(ssm.scroll) > 0.1f || !this.SlugcatUnlocked(this.colorFromIndex(this.slugcatPageIndex)));
            if (Mathf.Abs(ssm.lastScroll) > 0.5f && Mathf.Abs(ssm.scroll) <= 0.5f)
            {
                this.UpdateCharacterUI();
            }
            if (ssm.scroll == 0f && ssm.lastScroll == 0f)
            {
                if(ssm.quedSideInput != 0)
                {
                    var sign = (int)Mathf.Sign(ssm.quedSideInput);
                    ssm.slugcatPageIndex += sign;
                    ssm.slugcatPageIndex = (ssm.slugcatPageIndex + ssm.slugcatPages.Count) % ssm.slugcatPages.Count;
                    ssm.scroll = -sign;
                    ssm.lastScroll = -sign;
                    ssm.quedSideInput -= sign;
                    return;
                }
            }
        }

        private void OnLobbyAvailable()
        {
            startButton.buttonBehav.greyedOut = false;
        }

        private void StartGame()
        {
            RainMeadow.DebugMe();
            if (OnlineManager.lobby == null || !OnlineManager.lobby.isActive) return;
            manager.arenaSitting = null;
            manager.rainWorld.progression.ClearOutSaveStateFromMemory();
            manager.menuSetup.startGameCondition = ProcessManager.MenuSetup.StoryGameInitCondition.New;
            manager.RequestMainProcessSwitch(ProcessManager.ProcessID.Game);
        }

        public override void ShutDownProcess()
        {
            RainMeadow.DebugMe();
            if (OnlineManager.lobby != null) OnlineManager.lobby.OnLobbyAvailable -= OnLobbyAvailable;
            if (manager.upcomingProcess != ProcessManager.ProcessID.Game)
            {
                MatchmakingManager.instance.LeaveLobby();
            }
            base.ShutDownProcess();
        }

        public int GetCurrentlySelectedOfSeries(string series) // SelectOneButton.SelectOneButtonOwner
        {
            return 0;
        }

        public void SetCurrentlySelectedOfSeries(string series, int to) // SelectOneButton.SelectOneButtonOwner
        {
            return;
        }
    }
}
