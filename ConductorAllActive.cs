﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stylet
{
    public partial class Conductor<T>
    {
        /// <summary>
        /// Contains specific Conductor{T} collection types
        /// </summary>
        public partial class Collection
        {
            /// <summary>
            /// Conductor which has many items, all of which active at the same time
            /// </summary>
            public class AllActive : ConductorBase<T>
            {
                private BindableCollection<T> items = new BindableCollection<T>();

                /// <summary>
                /// All items associated with this conductor
                /// </summary>
                public IObservableCollection<T> Items
                {
                    get { return this.items; }
                }

                /// <summary>
                /// Creates a new Conductor{T}.Collection.AllActive
                /// </summary>
                public AllActive()
                {
                    this.items.CollectionChanged += (o, e) =>
                    {
                        switch (e.Action)
                        {
                            case NotifyCollectionChangedAction.Add:
                                this.ActivateAndSetParent(e.NewItems);
                                break;

                            case NotifyCollectionChangedAction.Remove:
                                this.CloseAndCleanUp(e.OldItems);
                                break;

                            case NotifyCollectionChangedAction.Replace:
                                this.ActivateAndSetParent(e.NewItems);
                                this.CloseAndCleanUp(e.OldItems);
                                break;

                            case NotifyCollectionChangedAction.Reset:
                                this.ActivateAndSetParent(this.items);
                                break;
                        }
                    };
                }

                /// <summary>
                /// Active all items in a given collection if appropriate, and set the parent of all items to this
                /// </summary>
                /// <param name="items">Items to manipulate</param>
                protected virtual void ActivateAndSetParent(IEnumerable items)
                {
                    this.SetParent(items);
                    if (this.IsActive)
                    {
                        foreach (var item in items.OfType<IActivate>())
                        {
                            item.Activate();
                        }
                    }
                }

                /// <summary>
                /// Activates all items whenever this conductor is activated
                /// </summary>
                protected override void OnActivate()
                {
                    foreach (var item in this.items.OfType<IActivate>())
                    {
                        item.Activate();
                    }
                }

                /// <summary>
                /// Deactivates all items whenever this conductor is deactivated
                /// </summary>
                protected override void OnDeactivate()
                {
                    foreach (var item in this.items.OfType<IDeactivate>())
                    {
                        item.Deactivate();
                    }
                }

                /// <summary>
                /// Close, and clean up, all items when this conductor is closed
                /// </summary>
                protected override void OnClose()
                {
                    // We've already been deactivated by this point    
                    foreach (var item in this.items)
                        this.CloseAndCleanUp(item);
                    
                    items.Clear();
                }

                /// <summary>
                /// Determine if the conductor can close. Returns true if and when all items can close
                /// </summary>
                public override Task<bool> CanCloseAsync()
                {
                    return this.CanAllItemsCloseAsync(this.items);
                }

                /// <summary>
                /// Activate the given item, and add it to the Items collection
                /// </summary>
                /// <param name="item">Item to activate</param>
                public override void ActivateItem(T item)
                {
                    if (item == null)
                        return;

                    this.EnsureItem(item);

                    if (this.IsActive)
                        ScreenExtensions.TryActivate(item);
                }

                /// <summary>
                /// Deactive the given item
                /// </summary>
                /// <param name="item">Item to deactivate</param>
                public override void DeactivateItem(T item)
                {
                    ScreenExtensions.TryDeactivate(item);
                }

                /// <summary>
                /// Close a particular item, removing it from the Items collection
                /// </summary>
                /// <param name="item">Item to close</param>
                public async override void CloseItem(T item)
                {
                    if (item == null)
                        return;

                    if (await this.CanCloseItem(item))
                    {
                        this.CloseAndCleanUp(item);
                        this.items.Remove(item);
                    }
                }

                /// <summary>
                /// Returns all children of this parent
                /// </summary>
                public override IEnumerable<T> GetChildren()
                {
                    return this.items;
                }

                /// <summary>
                /// Ensure an item is ready to be activated, by adding it to the items collection, as well as setting it up
                /// </summary>
                /// <param name="newItem">Item to ensure</param>
                protected override void EnsureItem(T newItem)
                {
                    if (!this.items.Contains(newItem))
                        this.items.Add(newItem);

                    base.EnsureItem(newItem);
                }
            }
        }
    }
}