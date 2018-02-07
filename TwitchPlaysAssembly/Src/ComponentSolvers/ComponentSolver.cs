﻿using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

public abstract class ComponentSolver
{
    public delegate IEnumerator RegexResponse(Match match);

    #region Constructors
    public ComponentSolver(BombCommander bombCommander, BombComponent bombComponent, CoroutineCanceller canceller)
	{
        BombCommander = bombCommander;
        BombComponent = bombComponent;
        Selectable = bombComponent.GetComponent<Selectable>();
        IRCConnection = IRCConnection.Instance;
        Canceller = canceller;
    
		if(bombCommander != null)
			HookUpEvents();
    }
    #endregion

    #region Interface Implementation
    public IEnumerator RespondToCommand(string userNickName, string message)
    {
		_responded = false;
        _processingTwitchCommand = true;
        if (Solved)
        {
            _processingTwitchCommand = false;
            yield break;
        }

        _currentUserNickName = userNickName;

        int beforeStrikeCount = StrikeCount;

		IEnumerator subcoroutine = null;
        if (message.StartsWith("send to module ", StringComparison.InvariantCultureIgnoreCase))
        {
            message = message.Substring(15);
        }
        else
        {
            subcoroutine = RespondToCommandCommon(message, userNickName);
        }

        if (subcoroutine == null || !subcoroutine.MoveNext())
        {
            if (_responded)
            {
                yield break;
            }

            try
			{
				subcoroutine = RespondToCommandInternal(message);
			}
			catch (Exception e)
			{
				HandleModuleException(e);
				yield break;
			}

			bool moved = false;
			if (subcoroutine != null)
			{
				try
				{
					moved = subcoroutine.MoveNext();

					if (moved && modInfo.DoesTheRightThing) _responded = true;
				}
				catch (Exception e)
				{
					HandleModuleException(e);
					yield break;
				}
			}

            if (subcoroutine == null || !moved || Solved || beforeStrikeCount != StrikeCount)
            {
                if (Solved || beforeStrikeCount != StrikeCount)
                {
                    IEnumerator focusDefocus = BombCommander.Focus(Selectable, FocusDistance, FrontFace);
                    while (focusDefocus.MoveNext())
                    {
                        yield return focusDefocus.Current;
                    }
                    yield return new WaitForSeconds(0.5f);

                    focusDefocus = BombCommander.Defocus(Selectable, FrontFace);
                    while (focusDefocus.MoveNext())
                    {
                        yield return focusDefocus.Current;
                    }
                    yield return new WaitForSeconds(0.5f);
                }
                else
                {
                    ComponentHandle.CommandInvalid(userNickName);
				}

                _currentUserNickName = null;
                _processingTwitchCommand = false;
                yield break;
            }
        }

        IEnumerator focusCoroutine = BombCommander.Focus(Selectable, FocusDistance, FrontFace);
        while (focusCoroutine.MoveNext())
        {
            yield return focusCoroutine.Current;
        }

        yield return new WaitForSeconds(0.5f);

        int previousStrikeCount = StrikeCount;
        bool parseError = false;
        bool needQuaternionReset = false;
	    bool hideCamera = false;
		bool exceptionThrown = false;
		
        while ((previousStrikeCount == StrikeCount || DisableOnStrike) && !Solved)
        {
			try
			{
				if (!subcoroutine.MoveNext())
				{
					break;
				}
				else
				{
					_responded = true;
				}
			}
			catch (Exception e)
			{
				exceptionThrown = true;
				HandleModuleException(e);
				break;
			}

            object currentValue = subcoroutine.Current;
            if (currentValue is string currentString)
            {
	            if (currentString.Equals("strike", StringComparison.InvariantCultureIgnoreCase))
                {
                    _delegatedStrikeUserNickName = userNickName;
                }
                else if (currentString.Equals("solve", StringComparison.InvariantCultureIgnoreCase))
                {
                    _delegatedSolveUserNickName = userNickName;
				}
				else if (currentString.Equals("unsubmittablepenalty", StringComparison.InvariantCultureIgnoreCase))
				{
					if (TwitchPlaySettings.data.UnsubmittablePenaltyPercent <= 0) continue;

					int penalty = Math.Max((int) (modInfo.moduleScore * TwitchPlaySettings.data.UnsubmittablePenaltyPercent), 1);
					ComponentHandle.leaderboard.AddScore(_currentUserNickName, -penalty);
					IRCConnection.SendMessage(TwitchPlaySettings.data.UnsubmittableAnswerPenalty, _currentUserNickName, ComponentHandle.idText.text, modInfo.moduleDisplayName, penalty, penalty > 1 ? "s" : "");
				}
				else if (currentString.StartsWith("strikemessage ", StringComparison.InvariantCultureIgnoreCase) && 
                    currentString.Substring(14).Trim() != string.Empty)
                {
                    StrikeMessage = currentString.Substring(14);
                }
                else if (currentString.Equals("parseerror", StringComparison.InvariantCultureIgnoreCase))
                {
                    parseError = true;
                    break;
                }
                else if (currentString.Equals("trycancel", StringComparison.InvariantCultureIgnoreCase) && 
                    Canceller.ShouldCancel)
                {
                    Canceller.ResetCancel();
                    break;
                }
                else if (currentString.StartsWith("sendtochat ", StringComparison.InvariantCultureIgnoreCase) && 
                    currentString.Substring(11).Trim() != string.Empty)
                {
                    IRCConnection.SendMessage(currentString.Substring(11));
                }
                else if (currentString.StartsWith("sendtochaterror ", StringComparison.InvariantCultureIgnoreCase) &&
                         currentString.Substring(16).Trim() != string.Empty)
                {
                    ComponentHandle.CommandError(userNickName, currentString.Substring(16));
                }
                else if (currentString.StartsWith("add strike", StringComparison.InvariantCultureIgnoreCase))
                {
                    OnStrike(null);
                }
                else if (currentString.Equals("multiple strikes", StringComparison.InvariantCultureIgnoreCase))
                {
                    DisableOnStrike = true;
                }
                else if (currentString.StartsWith("autosolve", StringComparison.InvariantCultureIgnoreCase))
                {
                    HandleModuleException(new Exception(currentString));
                    break;
                }
                else if (currentString.ToLowerInvariant().EqualsAny("detonate", "explode"))
                {
                    AwardStrikes(_currentUserNickName, BombCommander.StrikeLimit - BombCommander.StrikeCount);
                    BombCommander.twitchBombHandle.CauseExplosionByModuleCommand(string.Empty, modInfo.moduleDisplayName);
                    break;
                }
                else if (currentString.ToLowerInvariant().EqualsAny("elevator music", "hold music", "waiting music"))
                {
                    if (_musicPlayer == null)
                    {
                        _musicPlayer = MusicPlayer.StartRandomMusic();
                    }
                }
				else if (currentString.ToLowerInvariant().Equals("hide camera"))
	            {
		            if (!hideCamera)
		            {
				        BombMessageResponder.moduleCameras?.Hide();
				        BombMessageResponder.moduleCameras?.HideHUD();
			            IEnumerator hideUI = BombCommander.twitchBombHandle.HideMainUIWindow();
			            while (hideUI.MoveNext())
			            {
				            yield return hideUI.Current;
			            }
		            }
					hideCamera = true;
	            }
            }
            else if (currentValue is Quaternion localQuaternion)
            {
				BombCommander.RotateByLocalQuaternion(localQuaternion);
	            if (BombComponent.GetComponent<KMBombModule>()?.ModuleType.Equals("spwizPerspectivePegs") ?? false)
		            BombCommander.RotateCameraByLocalQuaternion(BombComponent, localQuaternion);
					//Whitelist perspective pegs as it only returns Quaternion.Euler(x,0,0), which is compatible with the RotateCamaraByQuaternion.
	            needQuaternionReset = true;
            }
			else if (currentValue is Quaternion[] localQuaternions)
            {
	            if (localQuaternions.Length == 2)
	            {
		            BombCommander.RotateByLocalQuaternion(localQuaternions[0]);
					BombCommander.RotateCameraByLocalQuaternion(BombComponent, localQuaternions[1]);
		            needQuaternionReset = true;
	            }
            }
            else if (currentValue is string[] currentStrings)
			{
				if (currentStrings.Length >= 1)
				{
					if (currentStrings[0].ToLowerInvariant().EqualsAny("detonate", "explode"))
					{
						AwardStrikes(_currentUserNickName, BombCommander.StrikeLimit - BombCommander.StrikeCount);
						switch (currentStrings.Length)
						{
							case 2:
								BombCommander.twitchBombHandle.CauseExplosionByModuleCommand(currentStrings[1], modInfo.moduleDisplayName);
								break;
							case 3:
								BombCommander.twitchBombHandle.CauseExplosionByModuleCommand(currentStrings[1], currentStrings[2]);
								break;
							default:
								BombCommander.twitchBombHandle.CauseExplosionByModuleCommand(string.Empty, modInfo.moduleDisplayName);
								break;
						}
						break;
					}
				}

			}
			yield return currentValue;
        }

		if (!_responded && !exceptionThrown)
		{
		    ComponentHandle.CommandInvalid(userNickName);
		}

        if (needQuaternionReset)
        {
			BombCommander.RotateByLocalQuaternion(Quaternion.identity);
	        BombCommander.RotateCameraByLocalQuaternion(BombComponent, Quaternion.identity);
        }

	    if (hideCamera)
	    {
		    BombMessageResponder.moduleCameras?.Show();
		    BombMessageResponder.moduleCameras?.ShowHUD();
		    IEnumerator showUI = BombCommander.twitchBombHandle.ShowMainUIWindow();
		    while (showUI.MoveNext())
		    {
			    yield return showUI.Current;
		    }
	    }

	    if (_musicPlayer != null)
        {
            _musicPlayer.StopMusic();
            _musicPlayer = null;
        }

	    if (DisableOnStrike)
	    {
		    AwardStrikes(_currentUserNickName, StrikeCount - previousStrikeCount);
		    DisableOnStrike = false;
	    }

        if(!parseError)
        {
            yield return new WaitForSeconds(0.5f);
        }

        IEnumerator defocusCoroutine = BombCommander.Defocus(Selectable, FrontFace);
        while (defocusCoroutine.MoveNext())
        {
            yield return defocusCoroutine.Current;
        }

        yield return new WaitForSeconds(0.5f);

        _currentUserNickName = null;
        _processingTwitchCommand = false;
    }
    #endregion

    #region Abstract Interface
    protected abstract IEnumerator RespondToCommandInternal(string inputCommand);
    #endregion

    #region Protected Helper Methods
    protected void DoInteractionStart(MonoBehaviour interactable)
    {
		interactable.GetComponent<Selectable>().HandleInteract();
    }

    protected void DoInteractionEnd(MonoBehaviour interactable)
    {
		Selectable selectable = interactable.GetComponent<Selectable>();
		selectable.OnInteractEnded();
		selectable.SetHighlight(false);
    }

    protected string GetModuleType()
    {
        KMBombModule bombModule = BombComponent.GetComponent<KMBombModule>();
        if (bombModule != null)
            return bombModule.ModuleType;
        KMNeedyModule needyModule = BombComponent.GetComponent<KMNeedyModule>();
        if (needyModule != null)
            return needyModule.ModuleType;
        return null;
    }

	protected WaitForSeconds DoInteractionClick(MonoBehaviour interactable, float delay) => DoInteractionClick(interactable, null, delay);

	protected WaitForSeconds DoInteractionClick(MonoBehaviour interactable, string strikeMessage=null, float delay=0.1f)
	{
	    if (strikeMessage != null)
	    {
	        StrikeMessage = strikeMessage;
	    }

        DoInteractionStart(interactable);
		DoInteractionEnd(interactable);
	    return new WaitForSeconds(delay);
	}

	protected void HandleModuleException(Exception e)
	{
		DebugHelper.LogException(e, "While solving a module an exception has occurred! Here's the error:");

		SolveModule("Looks like a module ran into a problem while running a command, automatically solving module.");
	}

	protected void SolveModule(string reason = "A module is being automatically solved.", bool removeSolveBasedModules = true)
	{
		IRCConnection.SendMessage("{0}{1}", reason, removeSolveBasedModules ? " Some other modules may also be solved to prevent problems." : "");

		_currentUserNickName = null;
		_delegatedSolveUserNickName = null;

		if(removeSolveBasedModules)
			TwitchComponentHandle.RemoveSolveBasedModules();
		CommonReflectedTypeInfo.HandlePassMethod.Invoke(BombComponent, null);
	}
	#endregion

	#region Private Methods
	private void HookUpEvents()
	{
	    BombComponent.OnPass += OnPass;
	    BombComponent.OnStrike += OnStrike;
		KMGameCommands gameCommands = BombComponent.GetComponentInChildren<KMGameCommands>();
		if (gameCommands == null) return;
		gameCommands.OnCauseStrike += x => { OnStrike(x); };
	}

    private bool OnPass(object _ignore)
    {
        //string componentType = ComponentHandle.componentType.ToString();
        //string headerText = (string)CommonReflectedTypeInfo.ModuleDisplayNameField.Invoke(BombComponent, null);
		if (modInfo == null)
            return false;

        int moduleScore = modInfo.moduleScore;
        if (modInfo.moduleScoreIsDynamic)
        {
            switch (modInfo.moduleScore)
            {
                case 0:
                    moduleScore = (BombCommander.bombSolvableModules) / 2;
                    break;
                default:
                    moduleScore = 5;
                    break;
            }
        }

	    if (BombComponent is NeedyComponent)
		    return false;

	    if (UnsupportedModule)
		    ComponentHandle?.idTextUnsupported?.gameObject.SetActive(false);

	    string solverNickname = null;
		if (_delegatedSolveUserNickName != null)
		{
			solverNickname = _delegatedSolveUserNickName;
            AwardSolve(_delegatedSolveUserNickName, moduleScore);
            _delegatedSolveUserNickName = null;
        }
        else if (_currentUserNickName != null)
		{
			solverNickname = _currentUserNickName;
            AwardSolve(_currentUserNickName, moduleScore);
        }

        BombCommander.bombSolvedModules++;
        BombMessageResponder.moduleCameras?.UpdateSolves();

        if (_turnQueued)
        {
            DebugHelper.Log("[ComponentSolver] Activating queued turn for completed module {0}.", Code);
            _readyToTurn = true;
            _turnQueued = false;
        }

	    ComponentHandle?.OnPass(solverNickname);

        BombMessageResponder.moduleCameras?.DetachFromModule(BombComponent, true);

        return false;
    }

    public IEnumerator TurnBombOnSolve()
    {
        while(_turnQueued)
            yield return new WaitForSeconds(0.1f);

        if (!_readyToTurn)
            yield break;

        while (_processingTwitchCommand)
            yield return new WaitForSeconds(0.1f);

        _readyToTurn = false;
        IEnumerator turnCoroutine = BombCommander.TurnBomb();
        while (turnCoroutine.MoveNext())
        {
            yield return turnCoroutine.Current;
        }

        yield return new WaitForSeconds(0.5f);
    }

	public void OnFakeStrike()
	{
		if (_delegatedStrikeUserNickName != null)
		{
			AwardStrikes(_delegatedStrikeUserNickName, 0);
			_delegatedStrikeUserNickName = null;
		}
		else if (_currentUserNickName != null)
		{
			AwardStrikes(_currentUserNickName, 0);
		}
		else if (ComponentHandle.PlayerName != null)
		{
			AwardStrikes(ComponentHandle.PlayerName, 0);
		}
	}

    private bool DisableOnStrike;
    private bool OnStrike(object _ignore)
    {
        //string headerText = (string)CommonReflectedTypeInfo.ModuleDisplayNameField.Invoke(BombComponent, null);
        StrikeCount++;
	    if (DisableOnStrike) return false;

		if (_delegatedStrikeUserNickName != null)
        {
            AwardStrikes(_delegatedStrikeUserNickName, 1);
            _delegatedStrikeUserNickName = null;
        }
        else if (_currentUserNickName != null)
        {
            AwardStrikes(_currentUserNickName, 1);
        }
        else if (ComponentHandle.PlayerName != null)
        {
            AwardStrikes(ComponentHandle.PlayerName, 1);
        }

        BombMessageResponder.moduleCameras?.UpdateStrikes(true);

        return false;
    }

    public bool OnStrikes(object _ignore)
    {
        StrikeCount++;
        BombMessageResponder.moduleCameras?.UpdateStrikes(true);
        return false;
    }

	public void SolveSilently()
	{
		_delegatedSolveUserNickName = null;
		_currentUserNickName = null;

		// TwitchComponentHandle.RemoveSolveBasedModules();
		CommonReflectedTypeInfo.HandlePassMethod.Invoke(BombComponent, null);
	}

    private void AwardSolve(string userNickName, int ComponentValue)
    {
        string headerText = UnsupportedModule ? modInfo.moduleDisplayName : BombComponent.GetModuleDisplayName();
        IRCConnection.SendMessage(TwitchPlaySettings.data.AwardSolve, Code, userNickName, ComponentValue, headerText);
        string RecordMessageTone = $"Module ID: {Code} | Player: {userNickName} | Module Name: {headerText} | Value: {ComponentValue}";
		Leaderboard.Instance?.AddSolve(userNickName);
	    if (!UserAccess.HasAccess(userNickName, AccessLevel.NoPoints))
	    {
		    Leaderboard.Instance?.AddScore(userNickName, ComponentValue);
	    }
	    else
	    {
		    TwitchPlaySettings.AddRewardBonus(ComponentValue);
	    }
		TwitchPlaySettings.AppendToSolveStrikeLog(RecordMessageTone);
        TwitchPlaySettings.AppendToPlayerLog(userNickName);
        if (OtherModes.timedModeOn)
        {
            float multiplier = OtherModes.getMultiplier();
            float time = multiplier * ComponentValue;
            BombCommander.timerComponent.TimeRemaining = BombCommander.CurrentTimer + time;
            IRCConnection.SendMessage("Bomb time increased by {0} seconds!", Math.Round(time, 1));
            if (multiplier < TwitchPlaySettings.data.TimeModeMaxMultiplier)
            {
                multiplier = multiplier + TwitchPlaySettings.data.TimeModeSolveBonus;
                OtherModes.setMultiplier(multiplier);
            }
        }
    }

    private void AwardStrikes(string userNickName, int strikeCount)
    {
	    string headerText = UnsupportedModule ? modInfo.moduleDisplayName : BombComponent.GetModuleDisplayName();
		int strikePenalty = modInfo.strikePenalty * (TwitchPlaySettings.data.EnableRewardMultipleStrikes ? strikeCount : 1);
        IRCConnection.SendMessage(TwitchPlaySettings.data.AwardStrike, Code, strikeCount == 1 ? "a" : strikeCount.ToString(), strikeCount == 1 ? "" : "s", 0, userNickName, string.IsNullOrEmpty(StrikeMessage) ? "" : " caused by " + StrikeMessage, headerText, strikePenalty);
        if (strikeCount <= 0) return;

        string RecordMessageTone = $"Module ID: {Code} | Player: {userNickName} | Module Name: {headerText} | Strike";
        TwitchPlaySettings.AppendToSolveStrikeLog(RecordMessageTone, TwitchPlaySettings.data.EnableRewardMultipleStrikes ? strikeCount : 1);
        
        int currentReward = TwitchPlaySettings.GetRewardBonus();
        currentReward = Convert.ToInt32(currentReward * .80);
        TwitchPlaySettings.SetRewardBonus(currentReward);
        IRCConnection.SendMessage("Reward reduced to " + currentReward + " points.");
        if (OtherModes.timedModeOn)
        {
            bool multiDropped = OtherModes.dropMultiplier();
            float multiplier = OtherModes.getMultiplier();
            string tempMessage;
            if (multiDropped)
            {
                tempMessage = "Multiplier reduced to " + Math.Round(multiplier, 1) + " and time";
            }
            else
            {
                tempMessage = $"Mutliplier set at {TwitchPlaySettings.data.TimeModeMinMultiplier}, cannot be further reduced.  Time";
            }
            if (BombCommander.CurrentTimer < (TwitchPlaySettings.data.TimeModeMinimumTimeLost / TwitchPlaySettings.data.TimeModeTimerStrikePenalty))
            {
                BombCommander.timerComponent.TimeRemaining = BombCommander.CurrentTimer - TwitchPlaySettings.data.TimeModeMinimumTimeLost;
                tempMessage = tempMessage + $" reduced by {TwitchPlaySettings.data.TimeModeMinimumTimeLost} seconds.";
            }
            else
            {
                float timeReducer = BombCommander.CurrentTimer * TwitchPlaySettings.data.TimeModeTimerStrikePenalty;
                double easyText = Math.Round(timeReducer, 1);
                BombCommander.timerComponent.TimeRemaining = BombCommander.CurrentTimer - timeReducer;
                tempMessage = tempMessage + $" reduced by {Math.Round(TwitchPlaySettings.data.TimeModeTimerStrikePenalty * 100, 1)}%. ({easyText} seconds)";
            }
            IRCConnection.SendMessage(tempMessage);
        }
        ComponentHandle.leaderboard.AddScore(userNickName, strikePenalty);
        ComponentHandle.leaderboard.AddStrike(userNickName, strikeCount);
        if (OtherModes.timedModeOn)
        {
            BombCommander.StrikeCount = 0;
            BombMessageResponder.moduleCameras.UpdateStrikes();
        }
        StrikeMessage = string.Empty;
    }
    #endregion

    public string Code
    {
        get;
        set;
    }

	public bool UnsupportedModule { get; set; } = false;
    
    #region Protected Properties

    protected string StrikeMessage
    {
        get;
        set;
    }

    protected bool Solved => BombComponent.IsSolved;

	protected bool Detonated => BombCommander.Bomb.HasDetonated;

	protected int StrikeCount { get; private set; } = 0;

	protected float FocusDistance
    {
        get
        {
            Selectable selectable = BombComponent.GetComponent<Selectable>();
            return selectable.GetFocusDistance();
        }
    }

    protected bool FrontFace
    {
        get
        {
            Vector3 componentUp = BombComponent.transform.up;
            Vector3 bombUp = BombCommander.Bomb.transform.up;
            float angleBetween = Vector3.Angle(componentUp, bombUp);
            return angleBetween < 90.0f;
        }
    }

    protected FieldInfo TryCancelField { get; set; }
    protected Type TryCancelComponentSolverType { get; set; }

    protected bool TryCancel
    {
        get
        {
            if (TryCancelField == null || TryCancelComponentSolverType == null ||
                !(TryCancelField.GetValue(TryCancelComponentSolverType) is bool))
                return false;
            return (bool)TryCancelField.GetValue(BombComponent.GetComponent(TryCancelComponentSolverType));
        }
        set
        {
            if (TryCancelField != null && TryCancelComponentSolverType != null &&
                (TryCancelField.GetValue(BombComponent.GetComponent(TryCancelComponentSolverType)) is bool))
                TryCancelField.SetValue(BombComponent.GetComponent(TryCancelComponentSolverType), value);
        }
    }
    #endregion

    #region Private Methods
    private IEnumerator RespondToCommandCommon(string inputCommand, string userNickName)
    {
        if (inputCommand.Equals("unview", StringComparison.InvariantCultureIgnoreCase))
        {
            cameraPriority = ModuleCameras.CameraNotInUse;
			BombMessageResponder.moduleCameras?.DetachFromModule(BombComponent);
            _responded = true;
        }
        else
        {
            if (inputCommand.StartsWith("view", StringComparison.InvariantCultureIgnoreCase))
            {
                _responded = true;
                bool pinAllowed = inputCommand.Equals("view pin", StringComparison.InvariantCultureIgnoreCase) &&
                                  (UserAccess.HasAccess(userNickName, AccessLevel.Mod, true) || modInfo.CameraPinningAlwaysAllowed);

                cameraPriority = (pinAllowed) ? ModuleCameras.CameraPinned : ModuleCameras.CameraPrioritised;
            }
            if ( (BombCommander.multiDecker) || (cameraPriority > ModuleCameras.CameraNotInUse))
            {
                BombMessageResponder.moduleCameras?.AttachToModule(BombComponent, ComponentHandle, Math.Max(cameraPriority, ModuleCameras.CameraInUse));
            }
        }

        if (inputCommand.Equals("show", StringComparison.InvariantCultureIgnoreCase))
		{
			yield return "show";
            yield return null;
        }
		else if (inputCommand.Equals("solve") && UserAccess.HasAccess(userNickName, AccessLevel.Admin, true) && !UnsupportedModule)
        {
	        SolveModule($"A module ({modInfo.moduleDisplayName}) is being automatically solved.", false);
        }
    }
    #endregion

    #region Readonly Fields
    protected readonly BombCommander BombCommander = null;
    protected readonly BombComponent BombComponent = null;
    protected readonly Selectable Selectable = null;
    protected readonly IRCConnection IRCConnection = null;
    public readonly CoroutineCanceller Canceller = null;
    #endregion

    #region Private Fields
    private string _delegatedStrikeUserNickName = null;
    private string _delegatedSolveUserNickName = null;
    private string _currentUserNickName = null;

    private MusicPlayer _musicPlayer = null;
    #endregion


    public ModuleInformation modInfo = null;
    public int cameraPriority = ModuleCameras.CameraNotInUse;

    public bool _turnQueued = false;
    private bool _readyToTurn = false;
    private bool _processingTwitchCommand = false;
	private bool _responded = false;

	public TwitchComponentHandle ComponentHandle = null;
}
