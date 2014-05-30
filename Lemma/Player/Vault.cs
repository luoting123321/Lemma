﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ComponentBind;
using Lemma.Util;
using Microsoft.Xna.Framework;

namespace Lemma.Components
{
	public class Vault : Component<Main>, IUpdateableComponent
	{
		public enum State
		{
			None,
			Straight,
			Down,
		}

		private Random random = new Random();

		// Input
		public Property<Vector3> Position = new Property<Vector3>();
		public Property<Vector3> FloorPosition = new Property<Vector3>();
		public Property<float> MaxSpeed = new Property<float>();
		public Property<WallRun.State> WallRunState = new Property<WallRun.State>();

		// Output
		public Property<State> CurrentState = new Property<State>();
		public Command LockRotation = new Command();
		public Property<float> LastSupportedSpeed = new Property<float>();
		public Command DeactivateWallRun = new Command();
		public Command<WallRun.State> ActivateWallRun = new Command<WallRun.State>();
		public AnimatedModel Model;

		// Input/output
		public BlockPredictor Predictor;
		public Property<float> Rotation = new Property<float>();
		public Property<bool> IsSupported = new Property<bool>();
		public Property<bool> HasTraction = new Property<bool>();
		public Property<bool> EnableWalking = new Property<bool>();
		public Property<bool> AllowUncrouch = new Property<bool>();
		public Property<bool> Crouched = new Property<bool>();
		public Property<Vector3> LinearVelocity = new Property<Vector3>();

		private float vaultTime;

		private bool vaultOver;
		
		private float moveForwardStartTime;
		private bool movingForward;

		private float walkOffEdgeTimer;
		private Vector3 originalPosition;
		private Vector3 vaultVelocity;
		private Vector3 forward;
		private Map map;
		private Map.Coordinate coord;

		public override void Awake()
		{
			base.Awake();
			this.Serialize = false;
		}

		public bool Go()
		{
			Matrix rotationMatrix = Matrix.CreateRotationY(this.Rotation);
			foreach (Map map in Map.ActivePhysicsMaps)
			{
				Direction up = map.GetRelativeDirection(Direction.PositiveY);
				Direction right = map.GetRelativeDirection(Vector3.Cross(Vector3.Up, -rotationMatrix.Forward));
				Vector3 pos = this.Position + rotationMatrix.Forward * -1.75f;
				Map.Coordinate baseCoord = map.GetCoordinate(pos).Move(up, 1);
				int verticalSearchDistance = this.IsSupported ? 2 : 3;
				foreach (int x in new[] { 0, -1, 1 })
				{
					Map.Coordinate coord = baseCoord.Move(right, x);
					for (int i = 0; i < verticalSearchDistance; i++)
					{
						Map.Coordinate downCoord = coord.Move(up.GetReverse());

						if (map[coord].ID != 0)
							break;
						else if (map[downCoord].ID != 0)
						{
							// Vault
							this.vault(map, coord);
							return true;
						}
						coord = coord.Move(up.GetReverse());
					}
				}
			}

			// Check block possibilities for vaulting
			foreach (BlockPredictor.Possibility possibility in this.Predictor.AllPossibilities)
			{
				Direction up = possibility.Map.GetRelativeDirection(Direction.PositiveY);
				Direction right = possibility.Map.GetRelativeDirection(Vector3.Cross(Vector3.Up, -rotationMatrix.Forward));
				Vector3 pos = this.Position + rotationMatrix.Forward * -1.75f;
				Map.Coordinate baseCoord = possibility.Map.GetCoordinate(pos).Move(up, 1);
				foreach (int x in new[] { 0, -1, 1 })
				{
					Map.Coordinate coord = baseCoord.Move(right, x);
					for (int i = 0; i < 4; i++)
					{
						Map.Coordinate downCoord = coord.Move(up.GetReverse());
						if (!coord.Between(possibility.StartCoord, possibility.EndCoord) && downCoord.Between(possibility.StartCoord, possibility.EndCoord))
						{
							this.Predictor.InstantiatePossibility(possibility);
							this.vault(possibility.Map, coord);
							return true;
						}
						coord = coord.Move(up.GetReverse());
					}
				}
			}

			return false;
		}

		private void vault(Map map, Map.Coordinate coord)
		{
			this.DeactivateWallRun.Execute();
			this.CurrentState.Value = State.Straight;

			this.coord = coord;
			const float vaultVerticalSpeed = 8.0f;

			Vector3 coordPosition = map.GetAbsolutePosition(coord);
			this.forward = coordPosition - this.Position;
			this.forward.Y = 0.0f;
			this.forward.Normalize();

			this.vaultVelocity = new Vector3(0, vaultVerticalSpeed, 0);

			this.map = map;

			DynamicMap dynamicMap = map as DynamicMap;
			if (dynamicMap != null)
			{
				BEPUphysics.Entities.Entity supportEntity = dynamicMap.PhysicsEntity;
				Vector3 supportLocation = this.FloorPosition;
				this.vaultVelocity += supportEntity.LinearVelocity + Vector3.Cross(supportEntity.AngularVelocity, supportLocation - supportEntity.Position);
			}

			// If there's nothing on the other side of the wall (it's a one-block-wide wall)
			// then vault over it rather than standing on top of it
			this.vaultOver = map[coordPosition + this.forward + Vector3.Down].ID == 0;

			this.LinearVelocity.Value = this.vaultVelocity;
			this.IsSupported.Value = false;
			this.HasTraction.Value = false;

			Vector3 dir = map.GetAbsoluteVector(map.GetRelativeDirection(this.forward).GetVector());
			this.Rotation.Value = (float)Math.Atan2(dir.X, dir.Z);
			this.LockRotation.Execute();

			this.EnableWalking.Value = false;
			this.Crouched.Value = true;
			this.AllowUncrouch.Value = false;

			Session.Recorder.Event(main, "Vault");
			this.Model.StartClip("Vault", 4, false, 0.1f);

			if (this.random.NextDouble() > 0.5)
				AkSoundEngine.PostEvent(AK.EVENTS.PLAY_PLAYER_GRUNT, this.Entity);

			this.vaultTime = 0.0f;
			this.moveForwardStartTime = 0.0f;
			this.movingForward = false;
			this.originalPosition = this.Position;
		}

		private void vaultDown(Vector3 forward)
		{
			this.forward = forward;
			this.vaultVelocity = this.forward * this.MaxSpeed;
			this.vaultVelocity.Y = this.LinearVelocity.Value.Y;
			this.LinearVelocity.Value = this.vaultVelocity;
			this.LockRotation.Execute();
			this.EnableWalking.Value = false;
			this.Crouched.Value = true;
			this.AllowUncrouch.Value = false;
			this.walkOffEdgeTimer = 0.0f;

			this.vaultTime = 0.0f;
			this.CurrentState.Value = State.Down;

			this.originalPosition = this.Position;
		}

		public bool TryVaultDown()
		{
			if (this.Crouched || !this.IsSupported)
				return false;

			Matrix rotationMatrix = Matrix.CreateRotationY(this.Rotation);
			bool foundObstacle = false;
			foreach (Map map in Map.ActivePhysicsMaps)
			{
				Direction down = map.GetRelativeDirection(Direction.NegativeY);
				Vector3 pos = this.Position + rotationMatrix.Forward * -1.75f;
				Map.Coordinate coord = map.GetCoordinate(pos);

				for (int i = 0; i < 5; i++)
				{
					if (map[coord].ID != 0)
					{
						foundObstacle = true;
						break;
					}
					coord = coord.Move(down);
				}

				if (foundObstacle)
					break;
			}

			if (!foundObstacle)
			{
				// Vault
				this.vaultDown(-rotationMatrix.Forward);
			}
			return !foundObstacle;
		}

		public void Update(float dt)
		{
			const float vaultVerticalSpeed = -8.0f;
			const float maxVaultTime = 0.5f;

			if (this.CurrentState == State.Down)
			{
				this.vaultTime += dt;

				bool delete = false;

				if (this.vaultTime > maxVaultTime) // Max vault time ensures we never get stuck
					delete = true;
				else if (this.walkOffEdgeTimer > 0.2f && this.IsSupported)
					delete = true; // We went over the edge and hit the ground. Stop.
				else if (!this.IsSupported) // We hit the edge, go down it
				{
					this.walkOffEdgeTimer += dt;

					if (this.walkOffEdgeTimer > 0.1f)
					{
						this.LinearVelocity.Value = new Vector3(0, vaultVerticalSpeed, 0);

						if (this.Position.Value.Y < this.originalPosition.Y - 3.0f)
							delete = true;
						else
						{
							this.ActivateWallRun.Execute(WallRun.State.Reverse);
							if (this.WallRunState.Value == WallRun.State.Reverse)
								delete = true;
						}
					}
				}

				if (this.walkOffEdgeTimer < 0.1f)
				{
					Vector3 velocity = this.forward * this.MaxSpeed;
					velocity.Y = this.LinearVelocity.Value.Y;
					this.LinearVelocity.Value = velocity;
				}

				if (delete)
				{
					this.AllowUncrouch.Value = true;
					this.EnableWalking.Value = true;
					this.CurrentState.Value = State.None;
				}
			}
			else if (this.CurrentState != State.None)
			{
				this.vaultTime += dt;

				bool delete = false;

				if (this.movingForward)
				{
					if (this.vaultTime - this.moveForwardStartTime > 0.25f)
						delete = true; // Done moving forward
					else
					{
						// Still moving forward
						this.LinearVelocity.Value = this.forward * this.MaxSpeed;
						this.LastSupportedSpeed.Value = this.MaxSpeed;
					}
				}
				else
				{
					// We're still going up.
					if (this.IsSupported || this.vaultTime > maxVaultTime || this.LinearVelocity.Value.Y < 0.0f
						|| (this.FloorPosition.Value.Y > this.map.GetAbsolutePosition(this.coord).Y + 0.1f)) // Move forward
					{
						// We've reached the top of the vault. Start moving forward.
						// Max vault time ensures we never get stuck

						if (this.vaultOver)
						{
							// If we're vaulting over a 1-block-wide wall, we need to keep the vaultMover alive for a while
							// to keep the player moving forward over the wall
							this.movingForward = true;
							this.moveForwardStartTime = this.vaultTime;
						}
						else
						{
							// We're not vaulting over a 1-block-wide wall
							// So just stop
							this.LinearVelocity.Value = forward * this.MaxSpeed;
							this.LastSupportedSpeed.Value = this.MaxSpeed;
							delete = true;
						}
					}
					else // We're still going up.
						this.LinearVelocity.Value = vaultVelocity;
				}

				if (delete)
				{
					this.map = null;
					this.CurrentState.Value = State.None;
					this.EnableWalking.Value = true;
					this.Entity.Add(new Animation
					(
						new Animation.Delay(0.1f),
						new Animation.Set<bool>(this.AllowUncrouch, true)
					));
				}
			}
		}
	}
}