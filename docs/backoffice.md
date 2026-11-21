# Using the backoffice

For editors. Feature Flags lets you switch parts of the site on and off, for everyone or for chosen members, without
waiting for a deploy. Developers add the flags; you decide who gets each one.

A flag usually controls one part of a page, such as a new button, a different search box or a member price, rather
than a whole page. That is what makes it safe to try something with a few members before everyone sees it.

## Finding the flags

Go to **Settings** and choose **Feature Flags** under **Advanced** in the tree.

<img src="images/feature-flags-list.png" alt="The Feature Flags list, grouped under Checkout and Members, each flag with its state, an on/off switch, notes and when it last changed">

The tag at the top shows which environment you are on, such as Staging or Production. **Changes apply to this
environment only**: switching a flag on in staging doesn't switch it on on the live site.

Flags are grouped under headings, such as Checkout, with a description under each. To find one:

- type in **Type to filter** to search names and descriptions;
- use the filters for state, group and tag;
- use **Show** to see only flags changed here, flags that differ from their default, flags declared twice, or
  undeclared flags;
- sort by group or by when they last changed;
- switch between a list and cards with the button at the top right.

The list shows 25 flags a page.

## What the list tells you

| | |
| --- | --- |
| **Off** | Nobody gets the feature. |
| **On for everyone** | Every visitor gets it, signed in or not. |
| **On for an audience** | Only the members and groups chosen, with how many under the state. |
| **Custom** | The developers set the flag up in a way this screen can't change yet, such as on between two dates. It can only be changed where it is declared. |
| **Changed here** | Someone changed the flag in this backoffice; the last column says who and when. |
| **Default** | Nobody has changed it here, so it is as the developers set it. |
| **Differs from default** | Its setting here is different from the developers' setting. |
| **Declared twice** | The developers declared the flag in two places. Worth telling them. |

## Switching a flag on or off

Use the switch in the list:

- **Turning a flag off** happens straight away.
- **Turning a flag on** first asks you to confirm, saying who it will be on for and on which environment.

Turning a flag off keeps who it was for, so turning it back on gives it to the same people again.

The site picks up a change within a few seconds, on every server.

## Choosing who a flag is for

Choose a flag's name to open it.

<img src="images/feature-flags-editor.png" alt="The editor for Checkout.ExpressPay: State set to On for an audience, three members in the audience, and an Info panel with its default and who last changed it">

1. Under **State**, choose **Off**, **On for everyone** or **On for an audience**.
2. For an audience, under **Audience** choose any of:
   - **Members:** particular people.
   - **Member groups:** everyone in a member group.
   - **Other groups:** groups the developers worked out in code, such as "Premium customers". Only shown when the site
     has some.
3. Choose **Save**. **Unsaved changes** shows at the top until you do.

A member gets the feature if they are in any of the chosen members or groups, and only while they are signed in.

The **Info** panel shows the flag's description, its default, where it was declared and who last changed it. The
**Definition** tab shows the flag exactly as it is stored, for developers.

## Going back to the default

Open the flag and choose **Reset to default**, then confirm. Your setting is removed and the flag goes back to the
developers' setting, which the confirmation names.

## Undeclared flags

When the developers remove a flag that someone had changed here, its setting is left behind with no effect. The
**Show** filter lists these as **Undeclared flags**, with a count. Choose **Remove** to delete one; this can't be
undone.

## When you can't change anything

- **You can see flags but not change them:** your user group has **View feature flags** but not **Change feature
  flags**. Ask an administrator.
- **The list says flags can't be changed here:** the developers have set this site so flags are only changed in
  its configuration. Every flag uses its default.
- **One flag can't be changed:** it is **Custom**. A box at the top of its editor says why.
