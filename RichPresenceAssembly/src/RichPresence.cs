using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RichPresenceAssembly
{
    public class RichPresence : MonoBehaviour
    {
		private DiscordRpc.RichPresence presence = new DiscordRpc.RichPresence();
	    private DiscordRpc.EventHandlers handlers;
	    private KMGameInfo _gameInfo;
	    private bool _enabled;
	    private bool Infinite;
	    private bool _factoryCheckComplete;
	    private bool factory;
	    private int _amountRemaining = 0;

	    private List<Bomb> Bombs = new List<Bomb>();

	    private void OnEnable()
	    {
		    handlers = new DiscordRpc.EventHandlers();
		    DiscordRpc.Initialize("523242657040826400", ref handlers, true, "341800");
		    presence.largeImageKey = "ktane";
		    presence.details = "Loading KTaNE";
		    DiscordRpc.UpdatePresence(presence);
			_enabled = true;
		    _gameInfo = GetComponent<KMGameInfo>();
		    _gameInfo.OnStateChange += StateChange;
	    }

	    private void OnDisable()
	    {
			DiscordRpc.Shutdown();
		    _enabled = false;
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

	    private void StateChange(KMGameInfo.State state)
	    {
		    switch (state)
		    {
				case KMGameInfo.State.Setup:
					StopCoroutine(FactoryCheck());
					StopCoroutine(WaitUntilEndFactory());
					Bombs.Clear();
					_factoryCheckComplete = false;
					_amountRemaining = 0;
					presence.details = "Setting up";
					DiscordRpc.UpdatePresence(presence);
					break;
				case KMGameInfo.State.Gameplay:
					StartCoroutine(WaitForBomb());
					StartCoroutine(FactoryCheck());
					break;
				case KMGameInfo.State.PostGame:
					StopCoroutine(FactoryCheck());
					StopCoroutine(WaitUntilEndFactory());
					Bombs.Clear();
					_factoryCheckComplete = false;
					_amountRemaining = 0;
					break;
		    }
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
		    }

		    yield return new WaitUntil(() => Bombs[0].GetTimer().IsUpdating);
		    presence.details = "Defusing" + (factory ? "| Factory" + (Infinite ? " Infinite" : " ") + " Mode" : "");
		    DateTime time = DateTime.UtcNow + TimeSpan.FromSeconds(Bombs[0].GetTimer().TimeRemaining);
			long unixTimestamp = (long)time.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
		    presence.endTimestamp = unixTimestamp;
		    presence.state = "Modules remaining: ";
		    presence.partySize = presence.partyMax = Bombs[0].BombComponents.Count(x => x.IsSolvable);
			DiscordRpc.UpdatePresence(presence);
	    }

		private bool OnPass(BombComponent component)
	    {
		    _amountRemaining--;
		    presence.partySize--;
			DiscordRpc.UpdatePresence(presence);
		    return false;
	    }

	    #region Factory Implementation
		private IEnumerator FactoryCheck()
		{
			yield return new WaitUntil(() => SceneManager.Instance.GameplayState.Bombs != null && SceneManager.Instance.GameplayState.Bombs.Count > 0);
			GameObject _gameObject = null;
			for (var i = 0; i < 4 && _gameObject == null; i++)
			{
				_gameObject = GameObject.Find("Factory_Info");
				yield return null;
			}

			if (_gameObject == null)
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

			List<UnityEngine.Object> factoryObject = FindObjectsOfType(_factoryType).ToList();

			if (factoryObject == null || factoryObject.Count == 0)
			{
				_factoryCheckComplete = true;
				yield break;
			}

			_factory = factoryObject[0];
			_gameroom = _gameModeProperty?.GetValue(_factory, new object[] { });
			if (_gameroom?.GetType() == _factoryInfiniteModeType)
			{
				Infinite = true;
				StartCoroutine(WaitUntilEndFactory());
			}

			if (_gameroom != null) factory = true;
			_factoryCheckComplete = true;
		}

		private UnityEngine.Object GetBomb => (UnityEngine.Object)_currentBombField.GetValue(_gameroom);

		private IEnumerator WaitUntilEndFactory()
		{
			yield return new WaitUntil(() => GetBomb != null);

			while (GetBomb != null)
			{
				UnityEngine.Object currentBomb = GetBomb;
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

		private static Type _factoryType = null;
		private static Type _factoryBombType = null;
		private static PropertyInfo _internalBombProperty = null;

		private static Type _factoryStaticModeType = null;
		private static Type _factoryFiniteModeType = null;
		private static Type _factoryInfiniteModeType = null;

		private static PropertyInfo _gameModeProperty = null;
		private static FieldInfo _currentBombField = null;

		private object _factory = null;
		private object _gameroom = null;
		#endregion
	}
}
