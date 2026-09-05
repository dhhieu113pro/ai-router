import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';
import { AdminSessionService } from './admin-session.service';

export const adminKeyInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(AdminSessionService);
  return next(session.hasKey
    ? request.clone({ setHeaders: { Authorization: `Bearer ${session.value}` } })
    : request);
};
