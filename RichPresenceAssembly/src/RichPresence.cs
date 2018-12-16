using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Assets.Scripts.Missions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RichPresenceAssembly
{
	public class RichPresence : MonoBehaviour
	{
		private DiscordRpc.RichPresence presence = new DiscordRpc.RichPresence();
		private DiscordRpc.EventHandlers handlers;
		private KMGameInfo _gameInfo;
		private KMBombInfo _bombInfo;
		private bool Infinite;
		private bool _factoryCheckComplete;
		private bool factory;
		private string _missionName;
		private bool _bombExploded;
		private List<Bomb> Bombs = new List<Bomb>();
		private bool _zenMode;
		private bool _timeMode;

		private void OnEnable()
		{
			handlers = new DiscordRpc.EventHandlers();
			DiscordRpc.Initialize("523242657040826400", ref handlers, true, "341800");
			presence.largeImageKey = "ktane";
			presence.details = "Loading KTaNE";
			DiscordRpc.UpdatePresence(presence);
			_gameInfo = GetComponent<KMGameInfo>();
			_bombInfo = GetComponent<KMBombInfo>();
			_gameInfo.OnStateChange += StateChange;
			_bombInfo.OnBombExploded += BombExploded;
		}

		private void OnDisable()
		{
			DiscordRpc.Shutdown();
			// ReSharper disable once DelegateSubtraction
			_gameInfo.OnStateChange -= StateChange;
		}

		private void Awake()
		{
			string path = ModManager.Instance.InstalledModInfos.First(x => x.Value.ID == "DiscordRichPresence")
				.Value.FilePath;
			// ReSharper disable once SwitchStatementMissingSomeCases
			switch (Application.platform)
			{
				case RuntimePlatform.WindowsPlayer:
					switch (IntPtr.Size)
					{
						case 4:
							//32 bit windows
							File.Copy(path + "\\dlls\\x86\\discord-rpc.dll",
								Application.dataPath + "\\Mono\\discord-rpc.dll", true);
							break;
						case 8:
							//64 bit windows
							File.Copy(path + "\\dlls\\x86_64\\discord-rpc.dll",
								Application.dataPath + "\\Mono\\discord-rpc.dll", true);
							break;
						default:
							throw new PlatformNotSupportedException("IntPtr size is not 4 or 8, what kind of system is this?");
					}
					break;
				case RuntimePlatform.OSXPlayer:
					File.Copy(path + "\\dlls\\discord-rpc.bundle",
						Application.dataPath + "\\Mono\\discord-rpc.bundle", true);
					break;
				case RuntimePlatform.LinuxPlayer:
					File.Copy(path + "\\dlls\\libdiscord-rpc.so",
						Application.dataPath + "\\Mono\\libdiscord-rpc.so", true);
					break;
				default:
					throw new PlatformNotSupportedException("The OS is not windows, linux, or mac, what kind of system is this?");
			}
		}

		private void BombExploded() => _bombExploded = true;

		private void StateChange(KMGameInfo.State state)
		{
			switch (state)
			{
				case KMGameInfo.State.Setup:
					StopCoroutine(FactoryCheck());
					StopCoroutine(WaitUntilEndFactory());
					SetupHandler();
					Bombs.Clear();
					_factoryCheckComplete = false;
					break;
				case KMGameInfo.State.Gameplay:
					StartCoroutine(WaitForBomb());
					StartCoroutine(FactoryCheck());
					break;
				case KMGameInfo.State.PostGame:
					StopCoroutine(FactoryCheck());
					StopCoroutine(WaitUntilEndFactory());
					PostGameHandler();
					Bombs.Clear();
					_factoryCheckComplete = false;
					break;
			}
		}

		private void SetupHandler()
		{
			presence.endTimestamp = presence.partyMax = presence.partySize = 0;
			presence.startTimestamp = 0;
			presence.state = null;
			presence.details = "Setting up";
			DiscordRpc.UpdatePresence(presence);
		}

		private void PostGameHandler()
		{
			presence.endTimestamp = presence.partyMax = presence.partySize = 0;
			presence.startTimestamp = 0;
			presence.state = null;
			presence.details = _missionName + " | " + (_bombExploded ? "Exploded" : "Solved");
			DiscordRpc.UpdatePresence(presence);
			_bombExploded = false;
		}

		private IEnumerator WaitForBomb()
		{
			yield return new WaitUntil(() => SceneManager.Instance.GameplayState.Bombs != null && SceneManager.Instance.GameplayState.Bombs.Count > 0);
			yield return new WaitForSeconds(2.0f);
			Bombs.AddRange(SceneManager.Instance.GameplayState.Bombs);

			yield return new WaitUntil(() => _factoryCheckComplete);
			foreach (BombComponent component in Bombs[0].BombComponents)
			{
				component.OnPass += OnPass;
				component.OnStrike += OnStrike;
			}

			if (!string.IsNullOrEmpty(GameplayState.MissionToLoad))
			{
				Debug.Log(GameplayState.MissionToLoad);
				_missionName = GameplayState.MissionToLoad == FreeplayMissionGenerator.FREEPLAY_MISSION_ID
					? "Freeplay"
					: SceneManager.Instance.GameplayState.Mission.DisplayName == "Custom Freeplay" //BombCreator
						? "Freeplay"
						: SceneManager.Instance.GameplayState.Mission.DisplayName;
			}
			else
				_missionName = "Freeplay";

			Type otherModesType = ReflectionHelper.FindType("OtherModes", "TwitchPlaysAssembly");
			if (otherModesType != null)
			{
				PropertyInfo zenModeInfo =
					otherModesType.GetProperty("ZenModeOn", BindingFlags.Public | BindingFlags.Static);
				_zenMode = (bool?)zenModeInfo?.GetValue(null, null) ?? false;
				PropertyInfo timeModeInfo =
					otherModesType.GetProperty("TimeModeOn", BindingFlags.Public | BindingFlags.Static);
				_timeMode = (bool?)timeModeInfo?.GetValue(null, null) ?? false;
			}
			else
			{
				otherModesType = ReflectionHelper.FindType("Tweaks", "TweaksAssembly");
				if (otherModesType != null)
				{
					List<Object> objects = FindObjectsOfType(otherModesType).ToList();
					// ReSharper disable once ConditionIsAlwaysTrueOrFalse
					if (!(objects == null || objects.Count == 0))
					{
						PropertyInfo currentModeInfo =
							otherModesType.GetProperty("CurrentMode", BindingFlags.Public | BindingFlags.Static);
						int? mode = (int?)currentModeInfo?.GetValue(objects[0], null);
						if (mode != null)
						{
							_timeMode = mode == 1;
							_zenMode = mode == 2;
						}
					}
				}
			}

			yield return new WaitUntil(() => Bombs[0].GetTimer().IsUpdating);
			presence.details = _missionName + (factory ? " | Factory" + (Infinite ? " Infinite" : "") + " Mode" : "") +
							   (_zenMode ? " | Zen Mode" : "") + (_timeMode ? " | Time Mode" : "");
			if (!_zenMode)
			{
				DateTime time = DateTime.UtcNow +
								TimeSpan.FromSeconds(Bombs[0].GetTimer().TimeRemaining / Bombs[0].GetTimer().GetRate());
				long unixTimestamp = (long)time.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
				presence.endTimestamp = unixTimestamp;
			}
			else
			{
				long unixTimestamp = (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
				presence.startTimestamp = unixTimestamp;
			}
			presence.state = "Modules remaining: ";
			presence.partySize = presence.partyMax = Bombs[0].BombComponents.Count(x => x.IsSolvable);
			DiscordRpc.UpdatePresence(presence);
		}

		private bool OnPass(BombComponent component)
		{
			presence.partySize--;
			DiscordRpc.UpdatePresence(presence);
			if (!_timeMode) return false;
			DateTime time = DateTime.UtcNow +
							TimeSpan.FromSeconds(Bombs[0].GetTimer().TimeRemaining / Bombs[0].GetTimer().GetRate());
			long unixTimestamp = (long)time.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
			presence.endTimestamp = unixTimestamp;
			DiscordRpc.UpdatePresence(presence);
			return false;
		}

		private bool OnStrike(BombComponent component)
		{
			if (_zenMode) return false;
			DateTime time = DateTime.UtcNow +
							TimeSpan.FromSeconds(Bombs[0].GetTimer().TimeRemaining / Bombs[0].GetTimer().GetRate());
			long unixTimestamp = (long)time.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
			presence.endTimestamp = unixTimestamp;
			DiscordRpc.UpdatePresence(presence);
			return false;
		}

		#region Factory Implementation
		private IEnumerator FactoryCheck()
		{
			yield return new WaitUntil(() => SceneManager.Instance.GameplayState.Bombs != null && SceneManager.Instance.GameplayState.Bombs.Count > 0);
			GameObject gameObject1 = null;
			for (int i = 0; i < 4 && gameObject1 == null; i++)
			{
				gameObject1 = GameObject.Find("Factory_Info");
				yield return null;
			}

			if (gameObject1 == null)
			{
				_factoryCheckComplete = true;
				yield break;
			}

			_factoryType = ReflectionHelper.FindType("FactoryAssembly.FactoryRoom");
			if (_factoryType == null)
			{
				_factoryCheckComplete = true;
				yield break;
			}

			_factoryBombType = ReflectionHelper.FindType("FactoryAssembly.FactoryBomb");
			_internalBombProperty = _factoryBombType.GetProperty("InternalBomb", BindingFlags.NonPublic | BindingFlags.Instance);

			_factoryStaticModeType = ReflectionHelper.FindType("FactoryAssembly.StaticMode");
			_factoryFiniteModeType = ReflectionHelper.FindType("FactoryAssembly.FiniteSequenceMode");
			_factoryInfiniteModeType = ReflectionHelper.FindType("FactoryAssembly.InfiniteSequenceMode");
			_currentBombField = _factoryFiniteModeType.GetField("_currentBomb", BindingFlags.NonPublic | BindingFlags.Instance);

			_gameModeProperty = _factoryType.GetProperty("GameMode", BindingFlags.NonPublic | BindingFlags.Instance);

			List<Object> factoryObject = FindObjectsOfType(_factoryType).ToList();

			// ReSharper disable once ConditionIsAlwaysTrueOrFalse
			if (factoryObject == null || factoryObject.Count == 0)
			{
				_factoryCheckComplete = true;
				yield break;
			}

			_factory = factoryObject[0];
			_gameRoom = _gameModeProperty?.GetValue(_factory, new object[] { });
			if (_gameRoom?.GetType() == _factoryInfiniteModeType)
			{
				Infinite = true;
				StartCoroutine(WaitUntilEndFactory());
			}

			if (_gameRoom != null) factory = true;
			_factoryCheckComplete = true;
		}

		private Object GetBomb => (Object)_currentBombField.GetValue(_gameRoom);

		private IEnumerator WaitUntilEndFactory()
		{
			yield return new WaitUntil(() => GetBomb != null);

			while (GetBomb != null)
			{
				Object currentBomb = GetBomb;
				Bomb bomb1 = (Bomb)_internalBombProperty.GetValue(currentBomb, null);
				yield return new WaitUntil(() => bomb1.HasDetonated || bomb1.IsSolved());

				Bombs.Clear();

				while (currentBomb == GetBomb)
				{
					yield return new WaitForSeconds(0.10f);
					if (currentBomb != GetBomb)
						continue;
					yield return new WaitForSeconds(0.10f);
				}

				StartCoroutine(WaitForBomb());
			}
		}
		//factory specific types

		private static Type _factoryType;
		private static Type _factoryBombType;
		private static PropertyInfo _internalBombProperty;

		private static Type _factoryStaticModeType;
		private static Type _factoryFiniteModeType;
		private static Type _factoryInfiniteModeType;

		private static PropertyInfo _gameModeProperty;
		private static FieldInfo _currentBombField;

		private object _factory;
		private object _gameRoom;
		#endregion
	}
}
