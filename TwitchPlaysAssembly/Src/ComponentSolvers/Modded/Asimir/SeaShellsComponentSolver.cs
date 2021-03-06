﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public class SeaShellsComponentSolver : ComponentSolver
{
	public SeaShellsComponentSolver(TwitchModule module) :
		base(module)
	{
		_component = module.BombComponent.GetComponent(ComponentType);
		_buttons = (KMSelectable[]) ButtonsField.GetValue(_component);
		ModInfo = ComponentSolverFactory.GetModuleInfo(GetModuleType(), "Press buttons by typing !{0} press alar llama 3. You can submit partial text as long it only matches one button. You can also use the button's position in english reading order. NOTE: Each button press is separated by a space so typing \"burglar alarm\" will press a button twice.");
	}

	protected internal override IEnumerator RespondToCommandInternal(string inputCommand)
	{
		string[] commands = inputCommand.ToLowerInvariant().Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

		if (commands.Length < 2 || !commands[0].Equals("press")) yield break;
		IEnumerable<string> buttonLabels = _buttons.Select(button => button.GetComponentInChildren<TextMesh>().text.ToLowerInvariant()).ToList();

		if (buttonLabels.Any(label => label == " ")) yield break;
		{
			yield return null;

			IEnumerable<string> submittedText = commands.Skip(1);
			List<KMSelectable> selectables = new List<KMSelectable>();
			foreach (string text in submittedText)
			{
				IEnumerable<string> matchingLabels = buttonLabels.Where(label => label.Contains(text)).ToList();

				int matchedCount = matchingLabels.Count();
				if (int.TryParse(text, out int index))
				{
					if (index < 1 || index > 5) yield break;

					selectables.Add(_buttons[index - 1]);
				}
				else
				{
					string fixedText = text;
					if (!buttonLabels.Contains(text))
					{
						switch (matchedCount)
						{
							case 1:
								fixedText = matchingLabels.First();
								break;
							case 0:
								yield return $"sendtochaterror There isn't any label that contains \"{text}\".";
								yield break;
							default:
								yield return
									$"sendtochaterror There are multiple labels that contain \"{text}\": {string.Join(", ", matchingLabels.ToArray())}.";
								yield break;
						}
					}

					selectables.Add(_buttons[buttonLabels.IndexOf(label => label == fixedText)]);
				}
			}

			int startingStage = (int) StageField.GetValue(_component);
			foreach (KMSelectable selectable in selectables)
			{
				DoInteractionClick(selectable);

				yield return new WaitForSeconds(0.1f);

				if (startingStage != (int) StageField.GetValue(_component))
					yield break;
			}
		}
	}

	static SeaShellsComponentSolver()
	{
		ComponentType = ReflectionHelper.FindType("SeaShellsModule");
		ButtonsField = ComponentType.GetField("buttons", BindingFlags.Public | BindingFlags.Instance);
		StageField = ComponentType.GetField("stage", BindingFlags.NonPublic | BindingFlags.Instance);
	}

	private static readonly Type ComponentType;
	private static readonly FieldInfo ButtonsField;
	private static readonly FieldInfo StageField;

	private readonly object _component;
	private readonly KMSelectable[] _buttons;
}
