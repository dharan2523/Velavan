import { Routes } from '@angular/router';
import { APP_ROLES } from './core/auth/auth.constants';
import { authGuard } from './core/auth/auth.guard';
import { guestGuard } from './core/auth/guest.guard';
import { roleGuard } from './core/auth/role.guard';
import { AdminLayoutComponent } from './layout/admin-layout/admin-layout.component';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () =>
      import('./features/auth/login/login.component').then(
        component => component.LoginComponent
      )
  },
  {
    path: '',
    component: AdminLayoutComponent,
    canActivate: [authGuard],
    children: [
      {
        path: '',
        data: { title: 'Dashboard' },
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then(
            component => component.DashboardComponent
          )
      },
      {
        path: 'users',
        canActivate: [roleGuard],
        data: {
          title: 'Users',
          roles: [APP_ROLES.superAdmin, APP_ROLES.admin]
        },
        loadComponent: () =>
          import('./features/users/user-list/user-list.component').then(
            component => component.UserListComponent
          )
      },
      {
        path: 'forbidden',
        data: { title: 'Access denied' },
        loadComponent: () =>
          import('./features/errors/forbidden/forbidden.component').then(
            component => component.ForbiddenComponent
          )
      }
    ]
  },
  {
    path: '**',
    redirectTo: ''
  }
];
